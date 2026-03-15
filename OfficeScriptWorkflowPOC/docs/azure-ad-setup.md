# Azure AD / Entra ID Setup

## Overview

This solution uses **Power Automate HTTP triggers secured by SAS (Shared Access Signature) keys**.
No OAuth token is required from the .NET Worker to call the flows — the SAS key embedded in the
trigger URL is the credential.

The Power Automate flow itself runs under a **service account** whose connection provides access
to SharePoint and Excel. No app registration API permissions for Graph/SharePoint are needed
on the Worker's side.

---

## Power Automate Service Account

The Power Automate flows use a dedicated service account connection:

- Create a dedicated M365 service account: `svc-officescript@yourtenant.onmicrosoft.com`
- Licence: Microsoft 365 E3 + Power Automate per-user Premium
- Create all flow connections under this service account
- Do NOT use personal accounts — connections break when staff leave

### SharePoint workbooks

Assign the service account **Contribute** permissions on the document library for each
SharePoint site that hosts a workbook:

```
SharePoint Admin Centre → Sites → Active sites → [your site]
  → Site membership → Add members → svc-officescript → Role: Member (Contribute)
```

Each workbook may be on a **different SharePoint site**. Repeat this for every site.

### SharePoint Embedded (SPE) workbooks

SPE containers do not use SharePoint site permissions. Membership is managed via
**Microsoft Graph API** on a per-container basis.

**Step 1 — Register an app to manage container permissions** (admin, one-time):

```
Azure Portal → Entra ID → App registrations → New registration
  Name: OfficeScriptWorkflow-SPEAdmin
  Supported account types: Accounts in this org only
  Redirect URI: none
```

Grant **Application** permissions (requires Global Admin or Application Admin consent):
- `FileStorageContainer.Selected` — access specific SPE containers
- `Sites.FullControl.All` — manage container memberships (or `Sites.ReadWrite.All`)

```bash
# Grant admin consent
az ad app permission admin-consent --id APP_ID
```

**Step 2 — Add the service account as a container member** (repeat per SPE container):

```bash
# Get a token for the SPE admin app
TOKEN=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)

# Add svc-officescript as a Writer on the container
curl -X POST "https://graph.microsoft.com/v1.0/storage/fileStorage/containers/CONTAINER_ID/permissions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "roles": ["writer"],
    "grantedToV2": {
      "user": {
        "userPrincipalName": "svc-officescript@YOURTENANT.onmicrosoft.com"
      }
    }
  }'
```

Valid roles: `reader`, `writer`, `manager`, `owner`.
Use `writer` for the service account — it allows reading and writing files without
managing container membership or settings.

**Step 3 — Verify access**

Sign in as `svc-officescript` and navigate to the container URL in a browser:
```
https://YOURTENANT.sharepoint.com/contentstorage/CSP_CONTAINERID
```
The workbook should be visible and openable in Excel for the Web.

---

## App Registration: `OfficeScriptWorkflowPOC-ScriptDeployer`

Used by `scripts/Deploy-OfficeScripts.ps1` to deploy Office Scripts to workbooks via the
Microsoft Graph beta API. Required for automated (CI/CD) deployment. Not needed at runtime
— the Worker Service never calls this.

| Field | Value |
|-------|-------|
| Name | `OfficeScriptWorkflowPOC-ScriptDeployer` |
| Supported account types | Accounts in this org only |
| Authentication | No redirect URIs (daemon app / device code) |

**API Permissions** — Application permissions (not delegated), admin consent required:

| Permission | Reason |
|-----------|--------|
| `Files.ReadWrite.All` | Write `.osts` script files to each service account's OneDrive (`Documents/Office Scripts/`) |

`Sites.ReadWrite.All` and `FileStorageContainer.Selected` are **not required** for script
deployment — the deploy script writes to OneDrive, not to SharePoint sites or SPE containers.

```
Azure Portal → Entra ID → App registrations → New registration
  Name: OfficeScriptWorkflowPOC-ScriptDeployer
  Supported account types: Accounts in this org only
  Redirect URI: none

→ API permissions → Add a permission → Microsoft Graph → Application permissions
  ☑ Files.ReadWrite.All

→ Grant admin consent
```

**Create a client secret:**

```
→ Certificates & secrets → New client secret
  Description: deploy-scripts-ci
  Expires: 24 months (or match your rotation policy)
```

Store the secret value as `GRAPH_CLIENT_SECRET` in your CI/CD pipeline secrets
(GitHub Actions → Settings → Secrets, or Azure DevOps → Variable group → secret variable).

**Pass to the deployment script:**

```powershell
# CI/CD pipeline
$secret = ConvertTo-SecureString $env:GRAPH_CLIENT_SECRET -AsPlainText -Force
pwsh scripts/Deploy-OfficeScripts.ps1 \
  -TenantId $env:TENANT_ID \
  -ClientId $env:CLIENT_ID \
  -ClientSecret $secret
```

---

## App Registration: `OfficeScriptWorkflowPOC-Worker` (Optional — future use)

Not needed for the current Power Automate + Office Scripts architecture. Only required
if the Worker Service needs to call Graph directly in the future (e.g. file upload,
reading container metadata):

| Field | Value |
|-------|-------|
| Name | `OfficeScriptWorkflowPOC-Worker` |
| Supported account types | Accounts in this org only |
| Authentication | No redirect URIs (daemon app) |

**API Permissions** (if direct Graph access is added):
- `Files.ReadWrite.All` — for SharePoint files via Graph
- `FileStorageContainer.Selected` — for SPE containers via Graph

---

## SAS Key Security

1. Copy the HTTP trigger URL from each Power Automate flow (it contains `sig=...`)
2. Store in **Azure Key Vault** using the double-hyphen convention (maps to `:` separators):

   **Per-workbook flow URLs** (one set per entry in `WorkbookRegistry:Workbooks`):
   ```
   WorkbookRegistry--Workbooks--0--InsertRangeFlowUrl
   WorkbookRegistry--Workbooks--0--UpdateRangeFlowUrl
   WorkbookRegistry--Workbooks--0--ExtractRangeFlowUrl
   WorkbookRegistry--Workbooks--0--BatchOperationFlowUrl   ← if using ExecuteBatchAsync()
   WorkbookRegistry--Workbooks--1--InsertRangeFlowUrl      ← second workbook, and so on
   ```

   **Service account pool flow URLs** (one set per account in `FlowAccountPool:Accounts`):
   ```
   FlowAccountPool--Accounts--0--InsertRangeFlowUrl
   FlowAccountPool--Accounts--0--UpdateRangeFlowUrl
   FlowAccountPool--Accounts--0--ExtractRangeFlowUrl
   FlowAccountPool--Accounts--0--BatchOperationFlowUrl
   FlowAccountPool--Accounts--1--InsertRangeFlowUrl        ← second pool account
   ```

   **Service Bus** (multi-replica only):
   ```
   ServiceBus--ConnectionString
   ```

3. Reference from `Program.cs` using `AddAzureKeyVault()` with `DefaultAzureCredential` — secrets
   merge automatically into the configuration hierarchy matching the double-hyphen paths above
4. The SAS key does not expire by default but can be regenerated in the flow settings
   ("Manage" → "Trigger history" → regenerate key) if compromised

---

## Local Development

Use `dotnet user-secrets` to avoid committing URLs. Keys match the `appsettings.json`
configuration path (colon-separated, array index in the path):

```bash
cd src/OfficeScriptWorkflow.Worker
dotnet user-secrets init

# First workbook (index 0) — per WorkbookRegistry
dotnet user-secrets set "WorkbookRegistry:Workbooks:0:InsertRangeFlowUrl"    "https://prod-XX...sig=YOUR_KEY"
dotnet user-secrets set "WorkbookRegistry:Workbooks:0:UpdateRangeFlowUrl"    "https://prod-XX...sig=YOUR_KEY"
dotnet user-secrets set "WorkbookRegistry:Workbooks:0:ExtractRangeFlowUrl"   "https://prod-XX...sig=YOUR_KEY"
dotnet user-secrets set "WorkbookRegistry:Workbooks:0:BatchOperationFlowUrl" "https://prod-XX...sig=YOUR_KEY"

# Second workbook (index 1) — if applicable
dotnet user-secrets set "WorkbookRegistry:Workbooks:1:InsertRangeFlowUrl"    "https://prod-XX...sig=YOUR_KEY"

# Service account pool (if using FlowAccountPool)
dotnet user-secrets set "FlowAccountPool:Accounts:0:InsertRangeFlowUrl"      "https://prod-XX...sig=YOUR_KEY"
dotnet user-secrets set "FlowAccountPool:Accounts:0:UpdateRangeFlowUrl"      "https://prod-XX...sig=YOUR_KEY"
dotnet user-secrets set "FlowAccountPool:Accounts:0:ExtractRangeFlowUrl"     "https://prod-XX...sig=YOUR_KEY"
dotnet user-secrets set "FlowAccountPool:Accounts:0:BatchOperationFlowUrl"   "https://prod-XX...sig=YOUR_KEY"

# Service Bus (multi-replica only)
dotnet user-secrets set "ServiceBus:ConnectionString" "Endpoint=sb://..."
```
