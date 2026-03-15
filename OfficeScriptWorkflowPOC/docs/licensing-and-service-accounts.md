# Licensing, Service Accounts & Configuration Reference

This document covers everything required before the first line of code can run in production:
licences for each component, service account setup, tenant-level admin configuration,
SharePoint permissions, Azure resource access policies, and rate limit considerations.

Cross-references to other docs are noted where a topic is covered in depth elsewhere.

---

## 1. Licence Requirements

### 1.1 Microsoft 365 — Service Account

The Power Automate flows run **under a service account**. That account must hold licences
that include both Office Scripts and Power Automate with Premium connectors.

| M365 Plan | Office Scripts | Power Automate (included) | HTTP Trigger (Premium) | Verdict |
|-----------|:--------------:|:-------------------------:|:----------------------:|:-------:|
| Microsoft 365 Business Basic | No | No | No | Not suitable |
| Microsoft 365 Business Standard | No | Per-user (standard) | No | Not suitable |
| Microsoft 365 Business Premium | Yes | Per-user (standard) | No | Not suitable |
| **Microsoft 365 E3** | **Yes** | **Per-user (standard)** | **No — needs add-on** | Add-on required |
| **Microsoft 365 E5** | **Yes** | **Per-user (standard)** | **No — needs add-on** | Add-on required |
| **Microsoft 365 E3/E5 + Power Automate Premium** | **Yes** | **Premium** | **Yes** | **Recommended** |
| Power Automate Process licence (standalone) | No | Premium | Yes | Requires separate M365 for Office Scripts |

**Minimum viable combination for the service account**:
- Microsoft 365 E3 **+** Power Automate per-user Premium plan (add-on)

**Recommended for production**:
- Microsoft 365 E3 **+** Power Automate per-user Premium plan

> **Why E3 and not Business plans?** Office Scripts is only included in M365 E3/E5 and
> select education/government plans. It is not available in Business Basic, Standard, or
> Premium plans regardless of Power Automate licence.

### 1.2 Power Automate — Premium Connectors

The HTTP trigger (`When an HTTP request is received`) is a **Premium connector**.
Any flow using it requires a Premium plan for the **flow owner** (the account whose
connection is used).

| Power Automate Plan | HTTP Trigger | Excel Online (Business) connector | Daily action limit |
|--------------------|:------------:|:---------------------------------:|:-----------------:|
| Seeded (included in M365) | No | Yes | 6,000 |
| **Per-user Premium** | **Yes** | **Yes** | **40,000** |
| Per-flow plan | Yes | Yes | 15,000 |
| Power Automate Process | Yes | Yes | Unlimited (capacity-based) |

For production workloads with multiple workbooks and frequent operations, the **per-user
Premium** plan is the baseline. If flow action volume exceeds 40,000/day per account,
use the **Power Automate Process** licence (charged per flow, not per user).

> **Action counting**: In a single flow run, the HTTP trigger + Run Script + Response =
> 3 actions. At 3 actions per call × N workbooks × M calls/day, stay within 40,000.
> Example: 10 workbooks × 100 calls/day × 3 actions = 3,000 actions/day — well within limit.

### 1.3 Office Scripts

Office Scripts requires:
1. Microsoft 365 plan that includes it (E3/E5 or eligible plans — see 1.1)
2. Admin tenant-level enablement (see Section 3.1)
3. Scripts must be **saved to the workbook** (not to OneDrive) to be callable from Power Automate

### 1.4 Azure Resources

| Azure Resource | Purpose | Pricing Model | Notes |
|---------------|---------|--------------|-------|
| **Azure Key Vault** | Store SAS-signed flow URLs and Service Bus connection strings | ~$0.03/10,000 operations | Standard tier is sufficient |
| **Azure Service Bus** | Distributed operation queue (multi-replica) | Standard tier: ~$0.10/million operations | Must use **Premium tier** if message size >256 KB or sessions need geo-redundancy |
| **Azure Container Apps** | Host Worker Service replicas | Consumption plan: pay per use | Recommended for KEDA autoscaling |
| **Azure Container Registry** | Store Docker images | Basic: ~$5/month | Standard if geo-replication needed |
| **Azure Monitor / App Insights** | Telemetry, log analytics | Pay per GB ingested | Optional but strongly recommended |

**Service Bus tier decision**:
- **Standard** — supports sessions (required), 256 KB message size, 10 GB storage. Sufficient for this solution.
- **Premium** — 1 MB message size, dedicated capacity, geo-recovery, private endpoints. Use if your organisation's security policy requires private networking.

---

## 2. Service Accounts

### 2.1 Power Automate Service Account

This is the most critical account in the solution. All Power Automate flows run under its
identity and its connections provide access to SharePoint and Excel.

**Account specification**:

| Property | Value |
|----------|-------|
| UPN format | `svc-officescript@YOURTENANT.onmicrosoft.com` |
| Display name | `SVC - Office Script Workflow` |
| Account type | Cloud-only (not synced from AD) |
| Department | IT / Platform Engineering |
| Usage location | Must match tenant's data residency region |
| Password | Long, random, stored in Key Vault |
| MFA | See Section 2.1.1 below |
| Licences | M365 E3 + Power Automate per-user Premium |

**Step-by-step creation (M365 Admin Centre)**:

1. Go to `admin.microsoft.com` → Users → Active users → Add a user
2. Fill in the display name and username (`svc-officescript`)
3. Uncheck "Automatically create a password" — set a strong random password (use a password manager)
4. Licence assignment:
   - Check **Microsoft 365 E3**
   - Check **Power Automate per user with attended RPA plan** (or equivalent Premium plan in your tenant)
5. Do NOT assign an Exchange mailbox if your policy allows it (reduces attack surface)
6. Complete creation

**SharePoint permissions** (assign after account creation):
```
SharePoint Admin Centre → Sites → Active sites → [your site]
  → Site membership → Add members
  → Add: svc-officescript@YOURTENANT.onmicrosoft.com
  → Role: Member (Contribute permission level)
```

> **Why Contribute and not Owner?** Contribute allows reading and writing files but
> not changing site structure or permissions. Principle of least privilege.

#### 2.1.1 MFA Handling for Service Accounts

Power Automate flows use **OAuth delegated** connections — not application credentials.
The service account must be able to authenticate non-interactively, which conflicts with
standard MFA enforcement.

**Recommended approach — Conditional Access exclusion**:

1. Microsoft Entra ID Admin Centre → Protection → Conditional Access
2. Open your MFA-requiring policy (or create a new named policy)
3. Under **Exclude** → Users and groups → add the service account
4. Add a compensating control:
   - Apply a named location restriction: only allow authentication from your tenant's Azure IP ranges
   - Enable **sign-in risk policy** for the account (Azure AD Identity Protection — P2 licence)

**Alternative — Workload Identity** (preferred if available in your tenant):
- Available with Azure AD P2 / Entra ID Governance
- Create a **Workload Identity** for Power Automate (currently in preview for Power Platform)
- Allows flows to run as a managed identity rather than a user account
- Eliminates MFA and password rotation concerns entirely

**What NOT to do**:
- Do not disable MFA globally for the service account without compensating controls
- Do not use a shared personal account — connections are tied to the account's session tokens
- Do not use `SecurityDefaults` exemptions — use proper Conditional Access exclusion

#### 2.1.2 Password & Token Rotation

Power Automate connections store OAuth refresh tokens. These expire if:
- The service account password is changed
- MFA or Conditional Access policy changes
- The account is disabled or deleted
- Refresh token lifetime policy is modified

**Operational procedure for password rotation**:
1. Generate new password (store in Key Vault)
2. Update the service account password
3. Open each affected Power Automate flow
4. Click the connection in the "Run script" action → **Fix connection**
5. Re-authenticate with the new credentials
6. Test each flow end-to-end
7. Document the rotation date

Set a recurring reminder every 90 days (or per your org's policy) to re-authenticate connections.

### 2.2 Worker Service Identity

The .NET Worker Service itself needs an identity to:
- Read flow URLs / secrets from Azure Key Vault
- Connect to Azure Service Bus (multi-replica mode)
- Write logs to Azure Monitor

**Option A — Managed Identity (recommended for Azure-hosted deployments)**:

When the Worker runs in Azure Container Apps, AKS, or an Azure VM, assign a **System-assigned
Managed Identity** or **User-assigned Managed Identity**.

```bash
# Azure Container Apps — enable managed identity at creation
az containerapp create \
  --name excel-operation-worker \
  --resource-group YOUR_RG \
  --environment office-script-env \
  --system-assigned \
  ...

# Grant Key Vault access to the managed identity
az keyvault set-policy \
  --name YOUR_KEYVAULT \
  --object-id $(az containerapp identity show \
      --name excel-operation-worker \
      --resource-group YOUR_RG \
      --query principalId -o tsv) \
  --secret-permissions get list

# Grant Service Bus access (if using Service Bus queue)
az role assignment create \
  --assignee $(az containerapp identity show \
      --name excel-operation-worker \
      --resource-group YOUR_RG \
      --query principalId -o tsv) \
  --role "Azure Service Bus Data Receiver" \
  --scope /subscriptions/SUB_ID/resourceGroups/YOUR_RG/providers/Microsoft.ServiceBus/namespaces/YOUR_NAMESPACE
```

**`Program.cs` update required to use Managed Identity with Key Vault**:

```csharp
// Add these packages:
// Azure.Identity
// Azure.Extensions.AspNetCore.Configuration.Secrets

using Azure.Identity;

var builder = Host.CreateApplicationBuilder(args);

// Reads secrets from Key Vault using the Managed Identity — no connection string needed
builder.Configuration.AddAzureKeyVault(
    new Uri("https://YOUR_KEYVAULT.vault.azure.net/"),
    new DefaultAzureCredential());

// The rest of the configuration merges Key Vault secrets into appsettings
// WorkbookRegistry__Workbooks__0__InsertRangeFlowUrl → WorkbookRegistry:Workbooks:0:InsertRangeFlowUrl
```

**Option B — Service Principal with client secret (for non-Azure or on-premises)**:

```bash
# Create an app registration
az ad app create --display-name "OfficeScriptWorker"

# Create a service principal
az ad sp create --id $(az ad app show --id OfficeScriptWorker --query appId -o tsv)

# Create a client secret (store output securely)
az ad app credential reset \
  --id APP_ID \
  --append \
  --years 1

# Grant Key Vault access
az keyvault set-policy \
  --name YOUR_KEYVAULT \
  --spn APP_ID \
  --secret-permissions get list
```

Store `ClientId`, `ClientSecret`, `TenantId` in the deployment environment — never in appsettings.json.

**Option C — Connection strings directly in Key Vault references (simplest)**:

For Azure-hosted workers, store the full Service Bus connection string and flow URLs as
Key Vault secrets, then reference them via Azure App Configuration or environment variable
injection from Key Vault at deployment time. The Worker reads them as plain strings — no
Azure SDK auth code changes needed.

---

## 3. Tenant-Level Admin Configuration

### 3.1 Enable Office Scripts

Office Scripts is **off by default** in most M365 tenants. An M365 Global Administrator
or a SharePoint Administrator must enable it.

**Steps**:
1. `admin.microsoft.com` → Settings → Org settings → Services tab
2. Search for **Office Scripts**
3. Check: "Let users automate their tasks in Office on the web"
4. Optionally check: "Let users share their Office Scripts in Power Automate"
   — **This option is required** for the "Run script" action to work in Power Automate
5. Save

> If "Let users share their Office Scripts in Power Automate" is not enabled, the Power
> Automate "Run script" action will fail with a permissions error even if the script exists
> in the workbook.

**Scope options** (if you want to limit enablement):
- Enable for all users (default when toggled on)
- Enable for a security group: add members of your team/service accounts to a group and
  apply the setting to that group only

### 3.2 Power Automate Environment

Power Automate organises flows into **environments**. By default, flows are created in the
**Default environment**.

**For production**, create a dedicated environment:

1. `admin.powerplatform.microsoft.com` → Environments → New
2. Name: `Excel-Script-Workflow-Prod`
3. Type: **Production**
4. Region: match your SharePoint/M365 data residency region
5. Create a Dataverse database: **No** (not needed for this solution)

**Assign environment roles**:

| Role | Who | What it allows |
|------|-----|---------------|
| Environment Admin | IT Platform team | Full control, can delete environments |
| Environment Maker | Service account (`svc-officescript`) | Create and edit flows |
| Basic User | Not needed for service account | Cannot create flows |

**Why a dedicated environment?**
- Prevents the service account's flows from appearing in colleagues' flow lists
- Allows separate DLP policies (see 3.3)
- Enables environment-level backup and restore
- Clean separation for governance and audit

### 3.3 Data Loss Prevention (DLP) Policies

Power Automate DLP policies control which connectors can be used together in a flow.
If your organisation has a DLP policy that blocks **HTTP** connector or **Excel Online (Business)**
connector, your flows will fail at creation time.

**Check existing DLP policies**:
1. `admin.powerplatform.microsoft.com` → Policies → Data policies
2. Review each policy that applies to your environment (tenant-wide and environment-specific)
3. Check whether **HTTP** and **Excel Online (Business)** are in the same tier

**Required connector grouping**:

| Connector | Required Tier |
|-----------|--------------|
| HTTP (`When an HTTP request is received`) | Business or Non-business |
| Excel Online (Business) | Business or Non-business |
| Both must be in the **same tier** | Otherwise the flow cannot be created |

**If they are in different tiers**, an M365/Power Platform admin must either:
- Move HTTP to the same tier as Excel Online (Business) in the tenant-wide policy
- Create an environment-specific DLP policy for your dedicated environment that
  overrides the tenant policy and places both connectors in the same tier

> **Note**: Moving HTTP to the Business tier allows it to share data with other Business
> connectors like SharePoint, Teams, Exchange — which is the correct grouping for enterprise use.

---

## 4. SharePoint Configuration

### 4.0 Storage Backend

Each workbook in the registry can be in a **different location**:

| Storage type | Location | `SiteUrl` format |
|---|---|---|
| `SharePoint` (default) | Standard SharePoint document library on any site | `https://TENANT.sharepoint.com/sites/SITENAME` |
| `SharePointEmbedded` | App-owned SPE container | `https://TENANT.sharepoint.com/contentstorage/CSP_CONTAINERID` |

Permissions differ significantly between the two types — see sections 4.1 (SharePoint) and 4.2 (SPE) below.

### 4.1 Site Permissions (SharePoint workbooks)

| Role | SharePoint Permission Level | What is allowed |
|------|-----------------------------|-----------------|
| Service account (`svc-officescript`) | **Contribute** | Read, write, upload, delete files in libraries |
| Worker Service identity (if direct Graph access added later) | **Read** | Read file metadata only |
| IT Admins | **Full Control / Site Owner** | Manage settings and permissions |

> Do not grant **Site Owner** to the service account. Owner permissions allow structural
> changes (deleting libraries, changing site settings) that are not needed and increase blast radius.

### 4.2 SharePoint Embedded — Container Permissions

SPE containers are not governed by SharePoint site permissions. Access is managed
via Microsoft Graph API on the container itself.

**Licensing requirement**: SharePoint Embedded requires a **SharePoint Embedded licence** assigned
to the container (not to users). Containers incur storage and transaction costs billed to the
application's Azure subscription — independent of M365 user licences.

| Actor | Required permission | How granted |
|---|---|---|
| Service account (`svc-officescript`) | `writer` role on each container | Graph API POST to container permissions |
| SPE Admin app registration | `FileStorageContainer.Selected` application permission | Entra ID app registration + admin consent |
| Tenant admin | Must enable SPE for the tenant | Microsoft 365 admin centre → Settings → SharePoint Embedded |

**Grant service account writer access to a container**:

```bash
# Using the SPE Admin app token (see docs/azure-ad-setup.md)
POST https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}/permissions
{
  "roles": ["writer"],
  "grantedToV2": {
    "user": { "userPrincipalName": "svc-officescript@YOURTENANT.onmicrosoft.com" }
  }
}
```

Repeat for every SPE container in `WorkbookRegistry:Workbooks` where `StorageType = SharePointEmbedded`.

Rather than granting site-level Contribute, restrict to the specific document library:

1. SharePoint site → Documents library (or your library name) → Settings (gear) → Library settings
2. Permissions for this document library → Stop Inheriting Permissions
3. Add the service account with **Contribute** level
4. Remove all other inherited groups (or keep site members with Read only)

This limits the service account to only the workbook library — it cannot access other
SharePoint content on the site.

### 4.3 External Sharing Settings

Ensure the SharePoint site is **not** configured for external sharing:

1. SharePoint Admin Centre → Sites → Active sites → [your site] → Policies tab
2. External sharing: set to **Only people in your organisation**

This prevents the workbook from being accidentally shared externally.

### 4.4 Versioning

Enable versioning on the document library to preserve workbook history:

1. Library settings → Versioning settings
2. Document version history: **Create major versions**
3. Keep: **50 major versions** (adjust per retention policy)

This protects against accidental data loss from script errors.

---

## 5. Azure Key Vault Configuration

### 5.1 Secret Naming Convention

Key Vault secret names use hyphens (not double-underscores). The .NET configuration system
maps hyphens in Key Vault names to the double-underscore hierarchy separator:

| Secret Name in Key Vault | Maps to appsettings.json path |
|--------------------------|------------------------------|
| `WorkbookRegistry--Workbooks--0--InsertRangeFlowUrl` | `WorkbookRegistry:Workbooks:0:InsertRangeFlowUrl` |
| `WorkbookRegistry--Workbooks--0--UpdateRangeFlowUrl` | `WorkbookRegistry:Workbooks:0:UpdateRangeFlowUrl` |
| `WorkbookRegistry--Workbooks--0--ExtractRangeFlowUrl` | `WorkbookRegistry:Workbooks:0:ExtractRangeFlowUrl` |
| `WorkbookRegistry--Workbooks--0--BatchOperationFlowUrl` | `WorkbookRegistry:Workbooks:0:BatchOperationFlowUrl` |
| `WorkbookRegistry--Workbooks--1--InsertRangeFlowUrl` | `WorkbookRegistry:Workbooks:1:InsertRangeFlowUrl` |
| `FlowAccountPool--Accounts--0--InsertRangeFlowUrl` | `FlowAccountPool:Accounts:0:InsertRangeFlowUrl` |
| `FlowAccountPool--Accounts--0--UpdateRangeFlowUrl` | `FlowAccountPool:Accounts:0:UpdateRangeFlowUrl` |
| `FlowAccountPool--Accounts--0--ExtractRangeFlowUrl` | `FlowAccountPool:Accounts:0:ExtractRangeFlowUrl` |
| `FlowAccountPool--Accounts--0--BatchOperationFlowUrl` | `FlowAccountPool:Accounts:0:BatchOperationFlowUrl` |
| `ServiceBus--ConnectionString` | `ServiceBus:ConnectionString` |

For each workbook in the registry, create up to four flow URL secrets (three required; `BatchOperationFlowUrl`
only if using `ExecuteBatchAsync()`). Increment the index (`0`, `1`, `2`...) to match the array index
in `WorkbookRegistry:Workbooks`. If using the `FlowAccountPool`, repeat the four-URL pattern for each
pool account, incrementing the `Accounts` index.

### 5.2 Access Policies vs RBAC

Key Vault supports two authorisation models:

**Vault Access Policies** (legacy — simpler):
```bash
az keyvault set-policy \
  --name YOUR_KEYVAULT \
  --object-id MANAGED_IDENTITY_PRINCIPAL_ID \
  --secret-permissions get list
```

**Azure RBAC** (recommended — integrates with Azure Policy and Entra ID PIM):
```bash
az role assignment create \
  --assignee MANAGED_IDENTITY_PRINCIPAL_ID \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/SUB/resourceGroups/RG/providers/Microsoft.KeyVault/vaults/YOUR_KEYVAULT
```

Use **Azure RBAC** if your organisation requires Just-In-Time access (PIM) or has Azure Policy
requiring RBAC on Key Vault resources.

### 5.3 Key Vault Firewall

In production, restrict Key Vault access to:
- Your Azure Container Apps environment's outbound IP ranges
- Azure Trusted Services (for diagnostic settings)

```bash
az keyvault network-rule add \
  --name YOUR_KEYVAULT \
  --resource-group YOUR_RG \
  --ip-address YOUR_CONTAINER_APP_OUTBOUND_IP/32
```

---

## 6. Azure Service Bus Configuration

Relevant only when `ServiceBus:UseServiceBus = true` (multi-replica deployments).

### 6.1 Queue Creation Requirements

The queue **must** be created with sessions enabled — this cannot be changed after creation.

```bash
az servicebus queue create \
  --namespace-name YOUR_NAMESPACE \
  --resource-group YOUR_RG \
  --name excel-operations \
  --enable-session true \
  --lock-duration PT6M \
  --max-delivery-count 3 \
  --default-message-time-to-live P1D \
  --enable-dead-lettering-on-message-expiration true \
  --enable-duplicate-detection false
```

| Parameter | Value | Reason |
|-----------|-------|--------|
| `--enable-session` | `true` | **Mandatory** — enables WorkbookId-based session routing |
| `--lock-duration` | `PT6M` (6 min) | Must exceed Office Script max runtime (5 min) |
| `--max-delivery-count` | `3` | Retries before dead-lettering a poisoned message |
| `--default-message-time-to-live` | `P1D` (1 day) | Unprocessed messages expire after 24 hours |
| `--enable-dead-lettering-on-message-expiration` | `true` | Expired messages go to DLQ for inspection |

### 6.2 Access Policies

Create separate policies for send (producer) and receive (consumer):

```bash
# Send — for any system that enqueues operations (could be same worker or external producer)
az servicebus namespace authorization-rule create \
  --namespace-name YOUR_NAMESPACE \
  --resource-group YOUR_RG \
  --name WorkerSend \
  --rights Send

# Listen — for the Worker Service
az servicebus namespace authorization-rule create \
  --namespace-name YOUR_NAMESPACE \
  --resource-group YOUR_RG \
  --name WorkerListen \
  --rights Listen

# Retrieve the Listen connection string — store in Key Vault
az servicebus namespace authorization-rule keys list \
  --namespace-name YOUR_NAMESPACE \
  --resource-group YOUR_RG \
  --name WorkerListen \
  --query primaryConnectionString -o tsv
```

**With Managed Identity (preferred)**:

Assign the built-in RBAC roles instead of connection-string-based policies:

| Role | Purpose |
|------|---------|
| `Azure Service Bus Data Sender` | Enqueue messages |
| `Azure Service Bus Data Receiver` | Dequeue and complete messages |

```bash
# Receiver for the Worker managed identity
az role assignment create \
  --assignee WORKER_MANAGED_IDENTITY_PRINCIPAL_ID \
  --role "Azure Service Bus Data Receiver" \
  --scope /subscriptions/SUB/resourceGroups/RG/providers/Microsoft.ServiceBus/namespaces/YOUR_NAMESPACE/queues/excel-operations
```

---

## 7. Rate Limits & Throttling Reference

Understanding limits prevents unexpected failures at scale.

### 7.1 Power Automate

| Limit | Standard Plan | Premium Plan | Notes |
|-------|:-------------:|:------------:|-------|
| Cloud flow actions per day | 6,000 | 40,000 | Per-user, resets at midnight UTC |
| Concurrent cloud flows | 5 | 100 | Per-user, across all flows |
| HTTP request trigger runs | 100/min | 250/min | Per flow |
| Flow run duration | 30 days | 30 days | Max wall-clock per run |
| HTTP request body size | 100 MB | 100 MB | Per trigger call |
| HTTP response timeout | 120 seconds (sync) | 120 seconds (sync) | Async 202 used for longer flows |

> **Monitoring**: `admin.powerplatform.microsoft.com` → Analytics → Power Automate
> shows per-user action consumption. Set up alerts before hitting 80% of the daily limit.

### 7.2 Office Scripts

| Limit | Value | Notes |
|-------|-------|-------|
| Script execution timeout | 5 minutes | Hard limit, cannot be extended |
| Workbook size | 100 MB | Performance degrades above ~30 MB |
| Cells per `setValues()` call | ~5 million | Practical limit before memory errors |
| Rows per `addRows()` call | ~10,000 | Recommended max; use `BatchSize: 500` for safety |
| Concurrent script executions | 1 per workbook | Excel locks the workbook during execution |
| Scripts per workbook | No documented limit | Practical: keep under 20 for manageability |

### 7.3 SharePoint / Excel Online

| Limit | Value |
|-------|-------|
| Max file size in SharePoint | 250 GB |
| Max workbook size for Excel Online | 25 MB (larger files may not open in browser) |
| Concurrent users editing | 1 in co-authoring for scripts; multiple for viewing |
| SharePoint REST API requests | 600 requests per minute per user |

### 7.4 Azure Service Bus (Standard Tier)

| Limit | Standard | Premium |
|-------|:--------:|:-------:|
| Message size | 256 KB | 1 MB |
| Queue size | 80 GB | 80 GB |
| Throughput | ~1,000 msg/sec | ~10,000 msg/sec |
| Sessions | Supported | Supported |
| Geo-disaster recovery | No | Yes |

---

## 8. Configuration Checklist

Use this as a pre-production sign-off checklist:

### Microsoft 365 / Power Platform

- [ ] Service account created (`svc-officescript@tenant`)
- [ ] M365 E3 licence assigned to service account
- [ ] Power Automate per-user Premium add-on licence assigned to service account
- [ ] Office Scripts enabled tenant-wide (`admin.microsoft.com` → Org settings → Office Scripts)
- [ ] "Let users share scripts in Power Automate" enabled
- [ ] Conditional Access exclusion configured for service account (with compensating controls)
- [ ] Service account MFA policy documented and reviewed by security team
- [ ] Dedicated Power Automate environment created (`Excel-Script-Workflow-Prod`)
- [ ] Service account added as Environment Maker in dedicated environment
- [ ] DLP policy verified: HTTP and Excel Online (Business) in the same data tier

### SharePoint workbooks

- [ ] Workbook uploaded to correct SharePoint document library
- [ ] Service account granted Contribute at library level (not site level) — repeat per site
- [ ] External sharing disabled on each SharePoint site
- [ ] Library versioning enabled (major versions, minimum 50)
- [ ] Office Scripts deployed to each workbook (see `docs/office-scripts-deployment.md`)

### SharePoint Embedded (SPE) workbooks — if applicable

- [ ] SPE enabled for the tenant (`admin.microsoft.com` → Settings → SharePoint Embedded)
- [ ] Container type registered with Microsoft and `ContainerTypeId` obtained
- [ ] SPE Admin app registration created in Entra ID with `FileStorageContainer.Selected` permission
- [ ] Admin consent granted for the SPE Admin app
- [ ] Each SPE container provisioned; `ContainerId` recorded for `appsettings.json`
- [ ] Service account granted `writer` role on each SPE container via Graph API
- [ ] `StorageType`, `ContainerId`, `ContainerTypeId`, and `SiteUrl` set correctly for each SPE workbook in registry
- [ ] Office Scripts deployed to each SPE-hosted workbook (open workbook via container URL in Excel for the Web)

### Power Automate Flows (per workbook)

- [ ] InsertRange flow created in dedicated environment
- [ ] UpdateRange flow created in dedicated environment
- [ ] ExtractRange flow created in dedicated environment
- [ ] BatchOperations flow created (required if using `ExecuteBatchAsync()`)
- [ ] All flows owned by / connections authenticated as service account
- [ ] SAS-signed trigger URLs copied and stored in Key Vault
- [ ] Each flow tested end-to-end from Power Automate portal
- [ ] Flow run history monitored for first 48 hours after go-live

### Service Account Pool (FlowAccountPool — only if batch volume exceeds 13,333 calls/day)

- [ ] Additional service accounts created (`svc-os-02`, etc.) with M365 E3 + PA Premium
- [ ] Each pool account granted SharePoint Contribute on the workbook library
- [ ] Each pool account's Conditional Access exclusion configured
- [ ] Duplicate flow set created per pool account (flows are hardcoded to connection owner)
- [ ] Pool account flow URLs stored in Key Vault (`FlowAccountPool--Accounts--N--*FlowUrl`)
- [ ] `FlowAccountPool:Accounts` array configured in `appsettings.json`
- [ ] Quota exhaustion behaviour verified (429 → pool routes to next account)

### Azure Resources

- [ ] Azure Key Vault created (Standard tier minimum)
- [ ] Key Vault firewall restricted to Worker Service outbound IPs
- [ ] All flow URLs stored as Key Vault secrets (naming convention in Section 5.1)
- [ ] Worker Service identity granted `Key Vault Secrets User` role
- [ ] Azure Service Bus namespace created (if multi-replica)
- [ ] Service Bus queue created with sessions enabled, lock duration ≥ 360s
- [ ] Worker Service identity granted `Azure Service Bus Data Receiver` role
- [ ] Azure Monitor / Application Insights workspace configured

### Worker Service

- [ ] `appsettings.json` contains no secrets (all secrets via Key Vault or user-secrets)
- [ ] Managed Identity configured on hosting resource (Container Apps / AKS / VM)
- [ ] `AddAzureKeyVault()` wired in `Program.cs` using `DefaultAzureCredential`
- [ ] `dotnet user-secrets` used for local development (not `.env` files)
- [ ] Docker image built and pushed to Azure Container Registry
- [ ] Container App / AKS deployment verified with at least one end-to-end operation
- [ ] KEDA scaler configured and tested (if using autoscaling)

---

## 9. Ongoing Operational Responsibilities

| Task | Frequency | Owner |
|------|-----------|-------|
| Re-authenticate Power Automate connections | Every 90 days (or after password change) | IT Platform |
| Review Power Automate action consumption per account | Monthly (alert at 80% of 40,000/day) | IT Platform |
| Review FlowAccountPool quota usage; add accounts if needed | Monthly | IT Platform |
| Rotate service account password (all pool accounts) | Per org policy (90–180 days) | IT Security |
| Review Key Vault access logs | Monthly | IT Security |
| Update Office Scripts after code changes | Per release | Development |
| Test flow connectivity after M365 updates | After each M365 release wave | IT Platform |
| Review dead-letter queue in Service Bus | Weekly (multi-replica) | IT Platform |
| Review SharePoint library storage | Quarterly | IT Platform |
