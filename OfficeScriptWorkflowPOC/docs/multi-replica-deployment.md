# Multi-Replica Deployment Guide

## Architecture Overview

```
                    ┌─────────────────────────────────────────┐
                    │           Azure Service Bus              │
                    │   Session-enabled queue: excel-ops       │
                    │                                          │
                    │  Session "workbook-01" ──────────────►  │
                    │  Session "workbook-02" ──────────────►  │
                    │  Session "workbook-03" ──────────────►  │
                    └────────────┬────────────────────────────┘
                                 │
              ┌──────────────────┼──────────────────┐
              │                  │                  │
              ▼                  ▼                  ▼
    ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
    │  Worker Replica  │  │  Worker Replica  │  │  Worker Replica  │
    │      Pod 1       │  │      Pod 2       │  │      Pod 3       │
    │                  │  │                  │  │                  │
    │ Owns sessions:   │  │ Owns sessions:   │  │ Owns sessions:   │
    │  workbook-01     │  │  workbook-02     │  │  workbook-03     │
    └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘
             │                     │                      │
             ▼                     ▼                      ▼
    ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
    │  Power Automate  │  │  Power Automate  │  │  Power Automate  │
    │  Flows (3 per    │  │  Flows (3 per    │  │  Flows (3 per    │
    │  workbook)       │  │  workbook)       │  │  workbook)       │
    └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘
             │                     │                      │
             ▼                     ▼                      ▼
    ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
    │   SharePoint     │  │   SharePoint     │  │   SharePoint     │
    │  Workbook-01     │  │  Workbook-02     │  │  Workbook-03     │
    │  .xlsx           │  │  .xlsx           │  │  .xlsx           │
    └──────────────────┘  └──────────────────┘  └──────────────────┘
```

## Processing Model — One Replica, One Workbook

**Rule**: Each worker replica processes operations for **one workbook at a time**, in sequence.
The replica completes all operations for that workbook before it picks up the next one.
Parallel throughput is achieved by running more replicas, not by parallelising within one replica.

```
Replica 1:  [wb-01: op1 → op2 → op3 → ... done] → [wb-04: op1 → op2 → ...]
Replica 2:  [wb-02: op1 → op2 → op3 → ... done] → [wb-05: op1 → op2 → ...]
Replica 3:  [wb-03: op1 → op2 → op3 → ... done] → [wb-06: op1 → op2 → ...]
```

**Why sequential within a workbook?**
Office Scripts lock the workbook during execution. If two operations ran concurrently
against the same workbook, the second script would fail or see stale state from the first.
Sequential processing eliminates this class of failure entirely.

**How concurrent workbooks are isolated** — Azure Service Bus session-enabled queues:

- `SessionId` = `WorkbookId` (set when enqueuing each operation)
- Service Bus guarantees that at most ONE receiver holds the session lock at any time
- All operations for `workbook-01` flow through a single active session receiver —
  regardless of how many replicas are running
- `MaxConcurrentSessions = 1` in configuration: each replica holds one session at a time
- If the session owner replica crashes, Service Bus releases the lock after the lock
  timeout and another replica picks it up automatically

---

## Azure Service Bus Setup

### Step 1 — Create a session-enabled queue

```bash
az servicebus queue create \
  --name excel-operations \
  --namespace-name YOUR_NAMESPACE \
  --resource-group YOUR_RG \
  --enable-session true \
  --lock-duration PT6M \        # 6 minutes — must exceed Office Script timeout (5 min)
  --default-message-time-to-live P1D \
  --max-delivery-count 3 \
  --enable-dead-lettering-on-message-expiration true
```

> `--lock-duration PT6M` is critical. If the message lock expires before the Office Script
> completes, Service Bus will redeliver the message and another replica will process it again.
> Office Scripts can run up to 5 minutes — set lock duration to at least 6 minutes.

### Step 2 — Create a connection string with Send+Listen permissions

```bash
az servicebus namespace authorization-rule keys list \
  --namespace-name YOUR_NAMESPACE \
  --resource-group YOUR_RG \
  --name RootManageSharedAccessKey \
  --query primaryConnectionString -o tsv
```

Store this in Azure Key Vault, NOT in appsettings.json.

### Step 3 — Store secrets in Key Vault

```bash
az keyvault secret set \
  --vault-name YOUR_KEYVAULT \
  --name "ServiceBus--ConnectionString" \
  --value "Endpoint=sb://yournamespace.servicebus.windows.net/;..."

# One entry per workbook per flow URL
az keyvault secret set \
  --vault-name YOUR_KEYVAULT \
  --name "WorkbookRegistry--Workbooks--0--InsertRangeFlowUrl" \
  --value "https://prod-XX...&sig=YOUR_SAS"
```

---

## Configuration for Multi-Replica

`appsettings.json` (base config, no secrets):
```json
{
  "WorkbookRegistry": {
    "Workbooks": [
      {
        "Id": "workbook-01",
        "DisplayName": "Primary Workbook",
        "SiteUrl": "https://tenant.sharepoint.com/sites/Finance",
        "WorkbookPath": "/Shared Documents/Financials.xlsx",
        "BatchSize": 500,
        "InsertRangeFlowUrl": "",
        "UpdateRangeFlowUrl": "",
        "ExtractRangeFlowUrl": ""
      },
      {
        "Id": "workbook-02",
        "DisplayName": "Risk Model",
        "SiteUrl": "https://tenant.sharepoint.com/sites/Risk",
        "WorkbookPath": "/Shared Documents/RiskModel.xlsx",
        "BatchSize": 200,
        "InsertRangeFlowUrl": "",
        "UpdateRangeFlowUrl": "",
        "ExtractRangeFlowUrl": ""
      }
    ]
  },
  "ServiceBus": {
    "UseServiceBus": true,
    "QueueName": "excel-operations",
    "MaxConcurrentSessions": 1,
    "MessageLockDurationSeconds": 360
  },
  "Concurrency": {
    "MaxPollingDurationMinutes": 10,
    "DefaultPollingIntervalSeconds": 10,
    "HttpTimeoutSeconds": 120
  }
}
```

Secrets are injected via Azure App Configuration or environment variables. The .NET configuration
system merges them: environment variable `WorkbookRegistry__Workbooks__0__InsertRangeFlowUrl`
overrides the empty string in appsettings.json.

---

## Azure Container Apps Deployment (Recommended)

Azure Container Apps natively supports:
- KEDA-based autoscaling triggered by Service Bus queue depth
- Managed identity (no connection string secrets in code)
- Rolling updates with zero downtime

### Step 1 — Build and push the Docker image

```bash
# Build
docker build -t officescriptworker:latest .

# Tag for Azure Container Registry
docker tag officescriptworker:latest YOURREGISTRY.azurecr.io/officescriptworker:latest

# Push
az acr login --name YOURREGISTRY
docker push YOURREGISTRY.azurecr.io/officescriptworker:latest
```

### Step 2 — Create Container Apps environment

```bash
az containerapp env create \
  --name office-script-env \
  --resource-group YOUR_RG \
  --location eastus
```

### Step 3 — Deploy with Service Bus KEDA scaler

```bash
az containerapp create \
  --name excel-operation-worker \
  --resource-group YOUR_RG \
  --environment office-script-env \
  --image YOURREGISTRY.azurecr.io/officescriptworker:latest \
  --min-replicas 1 \
  --max-replicas 10 \
  --scale-rule-name servicebus-scaler \
  --scale-rule-type azure-servicebus \
  --scale-rule-metadata "queueName=excel-operations" "namespace=YOUR_NAMESPACE" \
                         "messageCount=5" \
  --scale-rule-auth "connection=ServiceBus__ConnectionString" \
  --env-vars \
    "ServiceBus__UseServiceBus=true" \
    "ServiceBus__QueueName=excel-operations" \
    "ServiceBus__ConnectionString=secretref:servicebus-conn" \
  --secrets "servicebus-conn=YOUR_SB_CONNECTION_STRING"
```

The KEDA scaler adds one replica per 5 pending messages, up to 10 replicas.
With session-enabled queues, each new replica picks up a new workbook's session.

---

## Azure Kubernetes Service (AKS) Deployment

For existing AKS clusters, use KEDA with the Service Bus trigger:

```yaml
# keda-scaledobject.yaml
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata:
  name: excel-worker-scaler
spec:
  scaleTargetRef:
    name: excel-operation-worker
  minReplicaCount: 1
  maxReplicaCount: 10
  triggers:
  - type: azure-servicebus
    metadata:
      queueName: excel-operations
      namespace: YOUR_NAMESPACE
      messageCount: "5"        # Scale up every 5 pending messages
    authenticationRef:
      name: keda-servicebus-auth
```

```yaml
# deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: excel-operation-worker
spec:
  replicas: 1
  selector:
    matchLabels:
      app: excel-worker
  template:
    metadata:
      labels:
        app: excel-worker
    spec:
      containers:
      - name: worker
        image: YOURREGISTRY.azurecr.io/officescriptworker:latest
        env:
        - name: ServiceBus__UseServiceBus
          value: "true"
        - name: ServiceBus__ConnectionString
          valueFrom:
            secretKeyRef:
              name: servicebus-secret
              key: connection-string
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
```

---

## Windows Service Deployment (On-Premises)

For environments without Kubernetes/Container Apps:

```bash
# Publish self-contained executable
dotnet publish src/OfficeScriptWorkflow.Worker \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish

# Install as Windows Service (run as Administrator)
sc create "ExcelOperationWorker" \
   binpath="C:\Services\ExcelWorker\OfficeScriptWorkflow.Worker.exe" \
   start=auto \
   obj="DOMAIN\svc-excel-worker"

sc description "ExcelOperationWorker" "Excel Office Script orchestration worker"

# Configure service account with permissions to read Key Vault
# (use Managed Identity if Azure VM, or configure certificate-based auth)

sc start "ExcelOperationWorker"
```

For multi-replica on-premises: run the Windows Service on multiple VMs, all pointing
to the same Azure Service Bus namespace. Session-based locking handles the coordination.

---

## Capacity Planning

| Workbooks | Recommended Replicas | Notes |
|-----------|---------------------|-------|
| 1–3 | 1 | In-memory queue sufficient |
| 4–10 | 2–3 | Service Bus required |
| 11–30 | 3–5 | KEDA autoscaling recommended |
| 30+ | Scale per workbook | Consider dedicated workers per workbook group |

**Per replica**:
- One workbook session at a time (`MaxConcurrentSessions = 1`)
- Operations within that session processed strictly sequentially
- Replica throughput = one Office Script call at a time, polling until complete
- Add replicas to increase the number of workbooks processed in parallel

---

## Result Store for Multi-Replica Extract Operations

The current `InMemoryOperationResultStore` only works when the enqueuing code and the
worker run in the same process (single replica). For multi-replica scenarios where extract
operations (reads) return data to the caller:

**Option A — Redis-backed result store** (recommended):
- Implement `IOperationResultStore` using `StackExchange.Redis`
- Caller enqueues the operation, polls Redis keyed by `OperationId`
- Worker stores result in Redis after script completion
- TTL of 60 seconds on the Redis key to auto-clean

**Option B — Dedicated extract endpoint**:
- Extract operations are handled by a synchronous API call (not queued)
- Only write operations (Insert, Update) go through the queue
- Simpler but loses the ordering guarantee for mixed read-write sequences

**Option C — Callback URL**:
- Caller provides a webhook URL in the operation
- Worker POSTs the result to the callback URL on completion
- Works across replicas with no shared state

For the POC, Option A is the path to production. Option C is simplest if you control the caller.
