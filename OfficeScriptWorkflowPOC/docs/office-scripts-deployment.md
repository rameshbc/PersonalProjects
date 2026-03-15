# Office Scripts — Deployment Guide

## Overview

Office Scripts are TypeScript functions embedded inside Excel workbooks. They run inside
Excel's JavaScript engine on Microsoft's cloud infrastructure — not locally and not in the
Worker Service. Deployment means publishing a script file into the workbook's embedded
script library so it is available for the Power Automate "Run script" action.

Scripts can be deployed automatically via the Microsoft Graph beta `workbook/scripts` API,
or manually via Excel for the Web (fallback). Use the automated path for all routine
deployments.

---

## Automated Deployment (Recommended)

### Prerequisites

| Requirement | Detail |
|-------------|--------|
| PowerShell 7+ | `pwsh --version` — install from [github.com/PowerShell/PowerShell](https://github.com/PowerShell/PowerShell/releases) |
| App registration | `OfficeScriptWorkflowPOC-ScriptDeployer` — see `docs/azure-ad-setup.md` |
| Workbook | Already uploaded to the target SharePoint / SPE location |

### How to run

**Interactive (device code — for first-time setup or manual runs):**

```powershell
pwsh scripts/Deploy-OfficeScripts.ps1 \
  -TenantId YOUR_TENANT_ID \
  -ClientId YOUR_DEPLOYER_APP_ID
```

**CI/CD (client credentials — non-interactive):**

```powershell
$secret = ConvertTo-SecureString $env:GRAPH_CLIENT_SECRET -AsPlainText -Force
pwsh scripts/Deploy-OfficeScripts.ps1 \
  -TenantId $env:TENANT_ID \
  -ClientId $env:CLIENT_ID \
  -ClientSecret $secret
```

**Dry run (see planned actions without making Graph calls):**

```powershell
pwsh scripts/Deploy-OfficeScripts.ps1 -TenantId x -ClientId y -WhatIf
```

### Optional parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-AppSettingsPath` | `src/OfficeScriptWorkflow.Worker/appsettings.json` | Path to appsettings file |
| `-ScriptsDir` | `office-scripts/` | Directory containing `.ts` files |
| `-WhatIf` | off | Dry-run mode — no Graph calls made |

### What it does

For every service account UPN in `-ServiceAccountUpns`:

1. Resolves the service account's OneDrive via `GET /v1.0/users/{upn}/drive`
2. Ensures `Documents/Office Scripts/` folder exists in that OneDrive (creates it if missing)
3. For each `.ts` file in `office-scripts/`:
   - Wraps the TypeScript source in the `.osts` JSON envelope (`scriptVersion`, `apiVersion`, `script`)
   - `PUT /v1.0/drives/{driveId}/root:/Documents/Office%20Scripts/{ScriptName}.osts:/content`
   - This is an upsert — creates the file if new, overwrites it if already present
4. Prints a summary: accounts processed, scripts uploaded, errors

Errors on individual accounts or files are logged and skipped — the script continues rather
than aborting the entire run.

> **Why OneDrive, not the workbook?** Scripts stored in a service account's OneDrive
> ("My Scripts") are available to every Power Automate flow owned by that account,
> across all 2000 workbooks, without embedding scripts in each file. Deploy once per
> service account — not once per workbook.

### Scripts deployed

All 5 files in `office-scripts/` are deployed to every configured workbook:

| Script file | Script name in Excel |
|-------------|---------------------|
| `InsertRangeScript.ts` | `InsertRangeScript` |
| `UpdateRangeScript.ts` | `UpdateRangeScript` |
| `ExtractRangeScript.ts` | `ExtractRangeScript` |
| `ExtractDynamicArrayScript.ts` | `ExtractDynamicArrayScript` |
| `BatchOperationScript.ts` | `BatchOperationScript` |

> **Critical**: Script names must exactly match the name selected in each Power Automate
> flow's "Run script" action. Names are case-sensitive. The script derives the name from
> the filename without the `.ts` extension — do not rename the files.

### Service account access

Scripts uploaded to a service account's OneDrive are owned by that account. Power Automate
flows that run under that service account automatically have access — no extra permissions
are needed. The service account's existing SharePoint Contribute or SPE writer role grants
it the ability to read and write the workbooks; the "My Scripts" deployment is independent
of workbook-level permissions.

The `ScriptDeployer` app registration (used only at deploy time) never touches the workbooks
themselves — it writes `.osts` files to OneDrive. It does not need SharePoint or SPE permissions.

### Version control workflow

```
Developer edits office-scripts/InsertRangeScript.ts
    ↓
Code review / PR merge to main
    ↓
CI pipeline runs:
  pwsh scripts/Deploy-OfficeScripts.ps1 \
    -TenantId $TENANT_ID \
    -ClientId $CLIENT_ID \
    -ClientSecret $SECRET \
    -ServiceAccountUpns ($SERVICE_ACCOUNT_UPNS -split ',')
    ↓
Scripts uploaded to each service account's OneDrive ("My Scripts")
    ↓
All Power Automate flows that reference "My scripts" pick up the update immediately
    (no flow edits needed — the script name is unchanged)
```

Set `TENANT_ID`, `CLIENT_ID`, `GRAPH_CLIENT_SECRET`, and `SERVICE_ACCOUNT_UPNS` (comma-separated)
as secrets in your CI/CD pipeline (GitHub Actions secrets, Azure DevOps variable groups, etc.).

---

## Manual Deployment (Fallback)

Use this path when:
- The app registration for `ScriptDeployer` has not been set up yet
- You need to deploy a single script to a single workbook for troubleshooting
- The Graph beta `workbook/scripts` endpoint is unavailable

### Pre-requisites

| Requirement | Detail |
|-------------|--------|
| Browser | Edge or Chrome (Safari not supported for Automate tab) |
| Account | The service account that owns the Power Automate connections |
| Licence | M365 E3+ or standalone Office Scripts licence |
| Workbook | Already uploaded to the target SharePoint document library |
| Scripts | `.ts` files from `office-scripts/` in this repository |

### Step 1 — Open the workbook

1. Navigate to the SharePoint document library (e.g. `https://tenant.sharepoint.com/sites/YourSite/Shared Documents/`)
2. Click the workbook filename — it opens in **Excel for the Web** (do NOT use the desktop app)
3. Sign in as the **service account** that will own the Power Automate connection

### Step 2 — Open the Automate tab

1. Click **Automate** in the top ribbon
2. Click **New Script** — the Code Editor pane opens on the right
3. You will see a default empty `function main(workbook: ExcelScript.Workbook) {}` stub

### Step 3 — Paste the script

For each of the five scripts in the `office-scripts/` folder:

1. Select all text in the Code Editor and delete it
2. Open the corresponding `.ts` file from this repository and paste its contents
3. Click **Save Script** (Ctrl+S)
4. When prompted for a name, enter the filename **without the `.ts` extension**

### Step 4 — Run a smoke test in the browser

After saving each script:

1. Click **Run** in the Code Editor toolbar
2. Check the **Output** pane for any script errors

### Step 5 — Verify the script is visible in Power Automate

1. Open the Power Automate flow in a new tab
2. In the "Run script" action, click the Script dropdown
3. Confirm all scripts appear in the list for this workbook

---

## Updating an Existing Script

With automated deployment, updating a script is a standard code change:

1. Edit the `.ts` file in `office-scripts/`
2. Merge the PR to main
3. CI pipeline runs `Deploy-OfficeScripts.ps1` — the script is PATCH'd in place

The update is live immediately — no Power Automate flow changes needed.

> Scripts are saved inside the workbook file (`.xlsx`) itself, not in OneDrive or SharePoint
> separately. Copying the workbook file to a new location copies the scripts with it.

---

## Naming Conventions

| Script file | Script name in Excel | Used in Flow |
|-------------|---------------------|--------------|
| `InsertRangeScript.ts` | `InsertRangeScript` | InsertRange flow |
| `UpdateRangeScript.ts` | `UpdateRangeScript` | UpdateRange flow |
| `ExtractRangeScript.ts` | `ExtractRangeScript` | ExtractRange flow (static) |
| `ExtractDynamicArrayScript.ts` | `ExtractDynamicArrayScript` | ExtractRange flow (dynamic) |
| `BatchOperationScript.ts` | `BatchOperationScript` | BatchOperation flow |

---

## Limitations & Known Constraints

| Constraint | Detail |
|------------|--------|
| Execution timeout | 5 minutes absolute. Batch operations in `ExcelWorkbookService` stay under this |
| No network access | Scripts cannot call external APIs or URLs — sandboxed |
| No file system | Scripts cannot read/write files outside the workbook |
| Single workbook | Each script execution targets only the workbook it is embedded in |
| No secrets | Do not put credentials, URLs, or env-specific values in scripts |
| Concurrency | Excel for the Web locks the workbook during script execution. Concurrent executions for the same workbook will queue or fail — mitigated by Service Bus sessions in multi-replica deployments |
| Large data | `setValues()` on 100k+ cells can hit memory limits. Keep range writes < 50k cells per call |

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Script not visible in Power Automate "My scripts" tab | Scripts deployed to wrong account's OneDrive | Check that `-ServiceAccountUpns` matches the UPN of the account that owns the PA flow connection |
| Deploy script: HTTP 403 on OneDrive PUT | `Files.ReadWrite.All` admin consent not granted | Azure Portal → Entra ID → App registrations → `ScriptDeployer` → API permissions → Grant admin consent |
| Deploy script: HTTP 404 resolving `/users/{upn}/drive` | UPN typo, or account has no OneDrive licence | Verify UPN in Microsoft 365 admin centre; assign a licence with OneDrive if missing |
| Deploy script: `Documents/Office Scripts` folder missing | Service account has never opened Excel for the Web | Sign in once as the service account; Excel creates the folder automatically. The deploy script also creates it if absent. |
| `#SPILL!` error in ExtractDynamicArrayScript | A cell in the spill range is blocked | Clear blocking cells in the workbook |
| Script returns `success: false, error: "Sheet not found"` | Wrong sheet name in request | Check `sheetName` casing — exact match required |
| Power Automate "Run script" action fails with 400 | Parameter type mismatch (array arrived as string) | Add `json()` expression in flow parameter binding |
| Script times out | Row count too high | Lower `BatchSize` in WorkbookInstanceConfig |
