# Migration Scripts

One-time PowerShell scripts for migrating existing SharePoint infrastructure
to work with the new Managed Identity-based Document Manager.

---

## Scripts Overview

| Script | Purpose | Run once? |
|---|---|---|
| **Script A** — Grant MI Permissions | Grants the application's Managed Identity the `write` role on each existing SP site using `Sites.Selected` | Yes — idempotent, safe to re-run |
| **Script B** — Migrate SP → SPE | Copies all DocLibrary-A content from SharePoint Online to a pre-created SharePoint Embedded container | Yes — resumable, safe to re-run |

---

## Folder Structure

```
Migration/
├── Shared/
│   ├── Auth-Helpers.ps1          # Token acquisition (MI / ClientCreds / Interactive)
│   └── Graph-Helpers.ps1         # Throttle-aware Graph REST wrapper, paging, logging
├── ScriptA-GrantMIPermissions/
│   ├── Grant-ManagedIdentityPermissions.ps1
│   └── clients-template.csv      # Copy → clients.csv and populate
└── ScriptB-MigrateSpToSpe/
    ├── Migrate-SpToSpe.ps1
    └── migration-config-template.json  # Copy → migration-config.json and populate
```

---

## Prerequisites

### PowerShell version
PowerShell 7.2+ required (uses `??` null-coalescing and `foreach … in` syntax).

```powershell
$PSVersionTable.PSVersion  # Must be 7.2+
```

### Entra ID app registration (for Script A bootstrap)
An app registration or Managed Identity with:
- `Sites.FullControl.All` — to grant site-level permissions to the MI (Script A only, admin-consented)
- Alternatively: run Script A interactively as a SharePoint Admin

### Managed Identity app registration (the MI being configured)
The MI must already exist in Entra ID with `Sites.Selected` admin-consented.
Script A grants it access to specific sites — it does NOT create the app registration.

### For Script B additionally
- SPE containers for each client must be pre-created via the Admin portal.
- MI needs `Files.ReadWrite.All` on both SP and SPE drives.

---

## Script A — Grant MI Permissions

### Step 1: Populate clients.csv

```csv
ClientId,SiteUrl,SiteId,Notes
client-001,https://contoso.sharepoint.com/sites/client-001,,
client-002,https://contoso.sharepoint.com/sites/client-002,,
```

Leave `SiteId` blank — the script resolves it. Fill it in from the output to speed up re-runs.

### Step 2: Run (dry run first)

```powershell
cd Migration/ScriptA-GrantMIPermissions

# Dry run — logs what WOULD happen
.\Grant-ManagedIdentityPermissions.ps1 `
    -TenantId       "your-tenant-id" `
    -MiAppId        "your-mi-app-client-id" `
    -MiDisplayName  "DocManager-ManagedIdentity" `
    -ClientsCsvPath ".\clients.csv" `
    -AuthMode       Interactive `
    -WhatIf

# Real run
.\Grant-ManagedIdentityPermissions.ps1 `
    -TenantId       "your-tenant-id" `
    -MiAppId        "your-mi-app-client-id" `
    -MiDisplayName  "DocManager-ManagedIdentity" `
    -ClientsCsvPath ".\clients.csv" `
    -AuthMode       Interactive
```

### Unattended (CI/CD)

```powershell
$env:BOOTSTRAP_CLIENT_ID     = "sp-client-id"
$env:BOOTSTRAP_CLIENT_SECRET = "sp-client-secret"   # Never in source control

.\Grant-ManagedIdentityPermissions.ps1 `
    -TenantId               "your-tenant-id" `
    -MiAppId                "your-mi-app-client-id" `
    -MiDisplayName          "DocManager-ManagedIdentity" `
    -ClientsCsvPath         ".\clients.csv" `
    -AuthMode               ClientCredentials
```

### Output

- Console progress per site (GRANTED / SKIP / FAIL)
- JSON-Lines audit log: `Logs/ScriptA-GrantMI-{runId}.jsonl`

---

## Script B — Migrate SP → SPE Content

### Step 1: Populate migration-config.json

```powershell
Copy-Item .\migration-config-template.json .\migration-config.json
# Edit migration-config.json — set tenantId, auth, and client entries
```

### Step 2: Set credentials via environment variables

```powershell
$env:MI_APP_ID     = "your-mi-app-client-id"
$env:MI_APP_SECRET = "your-mi-app-secret"   # Or use ManagedIdentity mode
```

### Step 3: Dry run (always run dry first)

```powershell
cd Migration/ScriptB-MigrateSpToSpe

# behaviour.dryRun must be true in migration-config.json (it is by default)
.\Migrate-SpToSpe.ps1 -ConfigPath .\migration-config.json
```

Review the dry-run console output and the log file. Verify folder counts and file sizes match expectations.

### Step 4: Real migration

```powershell
# In migration-config.json: set "dryRun": false
# Then re-run:
.\Migrate-SpToSpe.ps1 -ConfigPath .\migration-config.json
```

### Step 5: Verify, then flip the backend toggle

After the client confirms the SPE content is correct, flip the storage backend
in the Admin portal (`/clients/provision` → Switch Backend) or via the API:

```http
PATCH /api/admin/clients/{clientId}/storage-backend
{ "storageBackend": "SharePointEmbedded" }
```

SP content is **never deleted** by this script — the SP library remains intact as a fallback.

### Resuming an interrupted run

The script writes `migration-state.json` after every file. If the run is interrupted,
simply re-run with the same config — already-completed items are skipped automatically.

### Output

- Console progress per client, folder, and file
- JSON-Lines audit log: `Logs/ScriptB-Migrate-{runId}.jsonl`
- State file: `migration-state.json` (resumability tracking)

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `Site not found` error in Script A | Wrong SiteUrl format in CSV | Use full URL: `https://tenant.sharepoint.com/sites/name` |
| `401 Unauthorized` | Token expired or wrong scope | Re-acquire token; check MI has Sites.Selected admin-consented |
| `429 Too Many Requests` | Graph throttling | Scripts handle this automatically with Retry-After; wait and re-run |
| Script B: file fails repeatedly | File > 250 MB or network interruption | Check `migration-state.json` for error detail; re-run to retry |
| Script B: SPE folder not found | containerId wrong in config | Verify SPE container ID in Admin portal |

---

## Security Notes

- **Never store secrets in config files.** Use environment variables or Azure Key Vault.
- The bootstrap app registration used for Script A needs elevated permissions (`Sites.FullControl.All`).
  Delete or disable the client secret immediately after the migration is complete.
- The MI app uses `Sites.Selected` (scoped, not tenant-wide) — minimum necessary privilege.
