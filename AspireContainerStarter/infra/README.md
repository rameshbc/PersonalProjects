# Infrastructure — Deployment & Security Reference

Covers the Bicep modules for Azure Container Apps and the ACR vulnerability
scanning setup. For the full project overview see the [root README](../README.md).

---

## Table of contents

- [Container Apps environment](#container-apps-environment)
- [Workload profiles](#workload-profiles)
- [Scaling rules](#scaling-rules)
- [RBAC requirements](#rbac-requirements)
- [Deployment steps](#deployment-steps)
- [Vulnerability scanning](#vulnerability-scanning)
- [Troubleshooting](#troubleshooting)

---

## Container Apps environment

The Bicep modules create one Container Apps Environment in **workload-profile mode**,
which allows mixing serverless (Consumption) replicas with dedicated compute pools
in the same environment.

```
infra/bicep/
├── main.bicep              ← orchestrator; call this for every deployment
├── main.bicepparam         ← default parameter values (edit before first deploy)
└── modules/
    ├── container-apps-env.bicep    ← environment + workload profile definitions
    ├── api-container-app.bicep     ← API (Consumption, HTTP scaling)
    └── worker-container-app.bicep  ← reusable worker module (dedicated, KEDA)
```

---

## Workload profiles

Three profiles are defined in `container-apps-env.bicep`:

| Profile name | SKU | vCPU/node | RAM/node | Assigned to | Resource/replica |
|---|---|---|---|---|---|
| `Consumption` | Shared | — | — | API | 0.5 vCPU / 1 Gi |
| `standard-worker` | D4 | 4 | 16 GB | Calc1Worker | 2 vCPU / 4 Gi |
| `heavy-worker` | D8 | 8 | 32 GB | Calc2Worker | 4 vCPU / 8 Gi |

**Node pool sizing** (`standardWorkerMaxNodes`, `heavyWorkerMaxNodes` in
`main.bicepparam`): this is the maximum number of *nodes* in each dedicated pool,
not replicas. A D4 node can run multiple worker replicas simultaneously as long as
the total CPU/memory requested fits.

### Adding a new compute tier

1. Add an entry to `workloadProfiles` in `container-apps-env.bicep`:

```bicep
{
  name: 'ultra-worker'
  workloadProfileType: 'D16'
  minimumCount: 0
  maximumCount: 5
}
```

2. Call the worker module from `main.bicep` with `workloadProfileName: 'ultra-worker'`
   and matching `cpu`/`memory` values.

No other file needs to change.

---

## Scaling rules

### API — HTTP concurrency (built-in KEDA)

```
minReplicas = 1    (always warm — no cold-start for users)
maxReplicas = 5
trigger: concurrentRequests = 50 per replica
```

### Workers — two complementary KEDA rules

**Primary — Service Bus queue depth**

| Parameter | Default | Effect |
|---|---|---|
| `messagesPerReplica` | 5 | Add one replica per N messages in the queue |
| `activationMessageCount` | 1 | First replica activates when ≥ 1 message exists |
| `minReplicas` | 0 | Scale to zero when queue is empty (workers cost nothing at idle) |
| `maxReplicas` | 50 | Upper bound |

Authentication is via `identity: system` — the Container App's system-assigned
managed identity reads queue metrics. No connection strings are stored.

**Secondary — CPU utilisation**

Fires when existing replicas exceed `cpuScaleThreshold` % CPU (default 70 %).
This adds extra replicas during compute-heavy bursts even if the queue depth
rule is already satisfied, preventing individual replicas from becoming the bottleneck.

Both rules co-exist in the same `scale.rules` array; KEDA evaluates them
independently and uses the maximum result.

---

## RBAC requirements

Every service uses its **system-assigned managed identity**. Assign these roles after
the Bicep deployment (principal IDs are in the deployment output):

| Resource | Role | Assigned to |
|---|---|---|
| Azure Container Registry | `AcrPull` | API, Calc1Worker, Calc2Worker |
| Service Bus namespace | `Azure Service Bus Data Receiver` | Calc1Worker, Calc2Worker |
| Service Bus namespace | `Azure Service Bus Data Sender` | API (publishes jobs) |
| Azure SQL Server | `db_datareader`, `db_datawriter` | API, Calc1Worker, Calc2Worker |
| Azure Cache for Redis | `Redis Cache Contributor` | API, Calc1Worker, Calc2Worker |
| App Configuration | `App Configuration Data Reader` | API, Calc1Worker, Calc2Worker |
| Key Vault | `Key Vault Secrets User` | API, Calc1Worker, Calc2Worker |

### Assign roles after Bicep deployment

```bash
# Capture principal IDs from deployment output
API_PRINCIPAL=$(az deployment group show -g <rg> -n main --query properties.outputs.apiPrincipalId.value -o tsv)
CALC1_PRINCIPAL=$(az deployment group show -g <rg> -n main --query properties.outputs.calc1WorkerPrincipalId.value -o tsv)
CALC2_PRINCIPAL=$(az deployment group show -g <rg> -n main --query properties.outputs.calc2WorkerPrincipalId.value -o tsv)

ACR_ID=$(az acr show -n <acr-name> -g <rg> --query id -o tsv)
SB_ID=$(az servicebus namespace show -n <sb-name> -g <rg> --query id -o tsv)

# ACR pull — all three services
for P in "$API_PRINCIPAL" "$CALC1_PRINCIPAL" "$CALC2_PRINCIPAL"; do
  az role assignment create --assignee "$P" --role "AcrPull" --scope "$ACR_ID"
done

# Service Bus receive — workers only
for P in "$CALC1_PRINCIPAL" "$CALC2_PRINCIPAL"; do
  az role assignment create --assignee "$P" --role "Azure Service Bus Data Receiver" --scope "$SB_ID"
done

# Service Bus send — API (publishes to calc1-jobs, calc2-jobs queues and job-progress topic)
az role assignment create --assignee "$API_PRINCIPAL" --role "Azure Service Bus Data Sender" --scope "$SB_ID"

# Azure SQL — API and both workers persist/read CalculationResults
SQL_SERVER_ID=$(az sql server show -n <sql-server-name> -g <rg> --query id -o tsv)
for P in "$API_PRINCIPAL" "$CALC1_PRINCIPAL" "$CALC2_PRINCIPAL"; do
  az sql db ad-admin create --server <sql-server-name> -g <rg> --display-name "mi-$P" --object-id "$P"
  # Or use contained database users:
  # CREATE USER [<mi-name>] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader ADD MEMBER [<mi-name>]; ALTER ROLE db_datawriter ADD MEMBER [<mi-name>];
done
```

---

## Deployment steps

### First deploy

```bash
# 1. Edit parameter defaults
#    Set registryName, serviceBusNamespace, containerAppsEnvName, location
vi infra/bicep/main.bicepparam

# 2. Deploy
az deployment group create \
  --resource-group <rg> \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.bicepparam \
  --parameters imageTag=<git-sha>

# 3. Assign RBAC (see commands above)

# 4. Set up vulnerability scanning
./infra/acr/register-tasks.sh \
  <acr-name> <rg> <github-pat> <org/repo>
```

### Redeploy (image update only)

The CD workflow handles this automatically. For manual image-only redeployment
without running the full Bicep template:

```bash
az containerapp update \
  --name aspire-api \
  --resource-group <rg> \
  --image <acr>.azurecr.io/api:<git-sha>

az containerapp update \
  --name aspire-calc1-worker \
  --resource-group <rg> \
  --image <acr>.azurecr.io/calc1-worker:<git-sha>

az containerapp update \
  --name aspire-calc2-worker \
  --resource-group <rg> \
  --image <acr>.azurecr.io/calc2-worker:<git-sha>
```

### Scale pool size (without full redeploy)

```bash
az containerapp env workload-profile set \
  --name <cae-name> \
  --resource-group <rg> \
  --workload-profile-name standard-worker \
  --max-nodes 20
```

---

## Vulnerability scanning

Three layers run independently:

```
Every push to main
       │
       ▼
┌─────────────────────────────────────────────────────┐
│ Layer 1 — GitHub Actions (cd.yml)  BLOCKING         │
│                                                     │
│  dotnet publish → local Docker daemon               │
│  aquasecurity/trivy-action                          │
│  severity: CRITICAL,HIGH  exit-code: 1              │
│  ignore-unfixed: true                               │
│  SARIF → GitHub Security tab                        │
│                                                     │
│  ✓ PASS → push to ACR                              │
│  ✗ FAIL → job stops, nothing pushed                │
└─────────────────────────┬───────────────────────────┘
                          │ image reaches ACR
                          ▼
┌─────────────────────────────────────────────────────┐
│ Layer 2 — Microsoft Defender for Containers         │
│           CONTINUOUS, automatic                     │
│                                                     │
│  Fires on every image pushed to ACR.               │
│  Findings → Defender for Cloud recommendations.    │
│  Enabled once via register-tasks.sh.               │
└─────────────────────────────────────────────────────┘

Daily at 02:00 UTC (independently of pushes)
       │
       ▼
┌─────────────────────────────────────────────────────┐
│ Layer 3 — ACR Task (scan-task.yaml + scan.sh)       │
│           SCHEDULED, reporting only                 │
│                                                     │
│  Authenticates via managed identity (IMDS)         │
│  Runs Trivy against api:latest, calc1-worker:latest,│
│  calc2-worker:latest                               │
│  Covers images that haven't been recently pushed   │
└─────────────────────────────────────────────────────┘
```

### ACR Task — how the auth works (`scan.sh`)

1. The task's managed identity calls the **Azure Instance Metadata Service (IMDS)**
   to obtain an ARM bearer token.
2. That token is exchanged at the **ACR OAuth2 token-exchange endpoint**
   (`/oauth2/exchange`) for an ACR refresh token.
3. The refresh token is passed to Trivy via `TRIVY_AUTH_URL` / `TRIVY_USERNAME` /
   `TRIVY_PASSWORD` environment variables.

No passwords or secrets are stored anywhere.

### ACR Task operations

```bash
# Register the task (run once)
./infra/acr/register-tasks.sh <acr> <rg> <gh-pat> <org/repo>

# Trigger manually
az acr task run --name vulnerability-scan --registry <acr>

# View logs from last run
az acr task logs --name vulnerability-scan --registry <acr>

# List run history
az acr task list-runs --name vulnerability-scan --registry <acr> -o table
```

---

## Troubleshooting

### Workers not scaling from zero

- Confirm the Container App's managed identity has **Azure Service Bus Data Receiver**
  on the Service Bus namespace (not just the queue).
- Check that `activationMessageCount: '1'` is present in the KEDA scale rule metadata.
- Verify the KEDA scaler can reach Service Bus:
  ```bash
  az containerapp logs show --name aspire-calc1-worker --resource-group <rg> --follow
  ```

### Trivy scan failing in CD

- Check the **GitHub Security** tab for the SARIF report to see which package/layer
  has the finding.
- For base image CVEs: update the SDK base image version in `global.json` to pull a
  patched runtime layer, then push again.
- For NuGet dependency CVEs: update the affected package in the relevant `.csproj`.

### ACR Task scan failing to authenticate

- Confirm the task was created with `--assign-identity`.
- Confirm the task's principal ID has `AcrPull` on the registry:
  ```bash
  PRINCIPAL=$(az acr task show --name vulnerability-scan --registry <acr> \
    --query identity.principalId -o tsv)
  az role assignment list --assignee "$PRINCIPAL" --all -o table
  ```

### Workload profile pool not scaling down to zero

- Dedicated pools (D4, D8) scale down nodes when no replicas are scheduled, but
  Azure may keep one node warm for a short period. This is expected behaviour.
- Set `minimumCount: 0` in the profile definition (already the default in
  `container-apps-env.bicep`) to allow full scale-down.
