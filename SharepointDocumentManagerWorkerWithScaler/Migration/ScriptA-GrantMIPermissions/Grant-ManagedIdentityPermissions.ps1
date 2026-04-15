<#
.SYNOPSIS
    Script A — Grant Managed Identity site-level permissions on existing SharePoint sites.

.DESCRIPTION
    One-time migration script that grants the application's Managed Identity (MI) the
    "write" role on each client's SharePoint site using the Graph API Sites.Selected approach.

    WHY Sites.Selected instead of Sites.ReadWrite.All?
    ───────────────────────────────────────────────────
    Sites.ReadWrite.All grants access to EVERY site in the tenant — a massive blast radius.
    Sites.Selected scopes the MI's access to ONLY the explicitly listed sites. This is
    Microsoft's recommended least-privilege approach for server-to-server SharePoint access.

    HOW IT WORKS
    ─────────────
    1. The operator runs this script with a SharePoint Admin account (Interactive mode)
       or a separate bootstrap service principal that has Sites.FullControl.All.
    2. For each client site in the CSV, the script calls:
         POST /sites/{siteId}/permissions
       with the MI's app registration ID and the desired role (write / read / owner).
    3. The MI's app registration must already exist in Entra ID with the
       Sites.Selected APPLICATION permission granted (admin-consented) — but NOT yet
       assigned to any specific sites. This script does the per-site assignment.
    4. Results are written to a JSON-Lines audit log for review.

    IDEMPOTENCY
    ───────────
    The script checks whether a permission entry for the MI already exists on each site
    before creating one. Safe to re-run — will skip already-granted sites and log them.

    PREREQUISITES
    ─────────────
    - Entra ID app registration for the MI with Sites.Selected granted (admin-consented).
    - The operator's account OR bootstrap service principal must be a SharePoint Admin.
    - clients.csv populated with site URLs for all existing client sites.

    USAGE
    ─────
    # Interactive (SharePoint Admin signs in):
    .\Grant-ManagedIdentityPermissions.ps1 `
        -TenantId         "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" `
        -MiAppId          "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy" `
        -MiDisplayName    "DocManager-ManagedIdentity" `
        -ClientsCsvPath   ".\clients-template.csv" `
        -AuthMode         Interactive `
        -WhatIf

    # Unattended (bootstrap service principal):
    .\Grant-ManagedIdentityPermissions.ps1 `
        -TenantId         $env:TENANT_ID `
        -MiAppId          $env:MI_APP_ID `
        -MiDisplayName    "DocManager-ManagedIdentity" `
        -ClientsCsvPath   ".\clients.csv" `
        -AuthMode         ClientCredentials `
        -BootstrapClientId     $env:BOOTSTRAP_CLIENT_ID `
        -BootstrapClientSecret $env:BOOTSTRAP_CLIENT_SECRET

.NOTES
    Author  : SharePoint Document Manager Team
    Version : 1.0.0
    Safe to re-run: YES (idempotent)
#>

[CmdletBinding(SupportsShouldProcess)]
param (
    # ── Required ──────────────────────────────────────────────────────────────

    [Parameter(Mandatory, HelpMessage = 'Entra ID tenant GUID')]
    [string] $TenantId,

    [Parameter(Mandatory, HelpMessage = 'App registration client ID of the Managed Identity app')]
    [string] $MiAppId,

    [Parameter(Mandatory, HelpMessage = 'Display name of the MI app (shown in SP permission UI)')]
    [string] $MiDisplayName,

    [Parameter(Mandatory, HelpMessage = 'Path to the clients CSV file')]
    [string] $ClientsCsvPath,

    # ── Auth ──────────────────────────────────────────────────────────────────

    [ValidateSet('Interactive', 'ClientCredentials', 'ManagedIdentity')]
    [string] $AuthMode = 'Interactive',

    # Required when AuthMode = ClientCredentials
    # This is a BOOTSTRAP service principal (separate from the MI being configured)
    # that has Sites.FullControl.All to grant per-site permissions.
    [string] $BootstrapClientId     = $env:BOOTSTRAP_CLIENT_ID,
    [string] $BootstrapClientSecret = $env:BOOTSTRAP_CLIENT_SECRET,

    # ── Behaviour ─────────────────────────────────────────────────────────────

    [ValidateSet('write', 'read', 'owner')]
    [string] $PermissionRole = 'write',

    [string] $LogDirectory = "$PSScriptRoot\Logs",
    [int]    $ThrottleSleepSeconds = 2   # Polite delay between sites (avoids burst throttling)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Load shared helpers ────────────────────────────────────────────────────────
. "$PSScriptRoot\..\Shared\Auth-Helpers.ps1"
. "$PSScriptRoot\..\Shared\Graph-Helpers.ps1"

# ── Setup ─────────────────────────────────────────────────────────────────────
$runId   = Get-Date -Format 'yyyyMMdd-HHmmss'
$logFile = Join-Path $LogDirectory "ScriptA-GrantMI-$runId.jsonl"

if (-not (Test-Path $LogDirectory)) {
    New-Item -ItemType Directory -Path $LogDirectory | Out-Null
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Script A: Grant MI Permissions" -ForegroundColor Cyan
Write-Host " Run ID  : $runId" -ForegroundColor Cyan
Write-Host " Log     : $logFile" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# ── Acquire token ─────────────────────────────────────────────────────────────
Write-Host "[Auth] Acquiring Graph token (mode: $AuthMode)..." -ForegroundColor Yellow

$tokenParams = @{ AuthMode = $AuthMode; TenantId = $TenantId }
if ($AuthMode -eq 'ClientCredentials') {
    $tokenParams.ClientId     = $BootstrapClientId
    $tokenParams.ClientSecret = $BootstrapClientSecret
}
$token = Get-GraphAccessToken @tokenParams
Write-Host "[Auth] Token acquired." -ForegroundColor Green

# ── Validate MI app exists ─────────────────────────────────────────────────────
Write-Host "[Validate] Looking up MI app registration '$MiAppId'..."
$miApp = Invoke-GraphRequest -Token $token -Uri "/applications?`$filter=appId eq '$MiAppId'"

if (-not $miApp.value -or $miApp.value.Count -eq 0) {
    throw "MI app registration '$MiAppId' not found in tenant '$TenantId'. " +
          "Ensure the app is registered and admin consent has been granted for Sites.Selected."
}
Write-Host "[Validate] MI app confirmed: $($miApp.value[0].displayName)" -ForegroundColor Green

# ── Load client list ───────────────────────────────────────────────────────────
if (-not (Test-Path $ClientsCsvPath)) {
    throw "Clients CSV not found: $ClientsCsvPath"
}
$clients = Import-Csv -Path $ClientsCsvPath
Write-Host "[Input] Loaded $($clients.Count) client(s) from CSV.`n"

# ── Counters ───────────────────────────────────────────────────────────────────
$results = @{
    Granted  = 0
    Skipped  = 0   # Already had permission
    Failed   = 0
    Total    = $clients.Count
}

# ══════════════════════════════════════════════════════════════════════════════
# MAIN LOOP — Process each client site
# ══════════════════════════════════════════════════════════════════════════════
foreach ($client in $clients) {
    $clientId  = $client.ClientId.Trim()
    $siteUrl   = $client.SiteUrl.Trim()
    $siteId    = $client.SiteId.Trim()

    Write-Host "── Processing client: $clientId ($siteUrl)" -ForegroundColor White

    try {
        # ── Step 1: Resolve Site ID if not provided in CSV ─────────────────
        if (-not $siteId) {
            Write-Verbose "  Resolving site ID from URL..."

            # Extract host + path from the URL for Graph site lookup
            # URL format: https://{tenant}.sharepoint.com/sites/{siteName}
            $uri     = [Uri]$siteUrl
            $host    = $uri.Host
            $path    = $uri.AbsolutePath.TrimStart('/')
            $lookupUri = "/sites/${host}`:/${path}"

            $site = Invoke-GraphRequest -Token $token -Uri $lookupUri

            if (-not $site) {
                throw "Site not found at URL '$siteUrl'. Check the URL in the CSV."
            }
            $siteId = $site.id
            Write-Verbose "  Resolved Site ID: $siteId"
        }

        # ── Step 2: Check if MI permission already exists (idempotency) ────
        Write-Verbose "  Checking existing permissions on site..."
        $existingPerms = Invoke-GraphRequestPaged -Token $token -Uri "/sites/$siteId/permissions"

        $alreadyGranted = $existingPerms | Where-Object {
            $_.grantedToIdentities | Where-Object {
                $_.application -and $_.application.id -eq $MiAppId
            }
        }

        if ($alreadyGranted) {
            $existingRole = ($alreadyGranted.roles -join ', ')
            Write-Host "  [SKIP] MI already has '$existingRole' permission on this site." -ForegroundColor DarkGray

            Write-MigrationLog -LogFile $logFile -Level 'INFO' `
                -Message "Permission already exists — skipped" `
                -ClientId $clientId -ResourceId $siteId `
                -Extra @{ siteUrl = $siteUrl; existingRole = $existingRole }

            $results.Skipped++
            continue
        }

        # ── Step 3: Grant the permission ───────────────────────────────────
        $permBody = @{
            roles              = @($PermissionRole)
            grantedToIdentities = @(
                @{
                    application = @{
                        id          = $MiAppId
                        displayName = $MiDisplayName
                    }
                }
            )
        }

        if ($PSCmdlet.ShouldProcess($siteUrl, "Grant '$PermissionRole' permission to MI '$MiAppId'")) {
            $granted = Invoke-GraphRequest `
                -Token  $token `
                -Uri    "/sites/$siteId/permissions" `
                -Method POST `
                -Body   $permBody

            Write-Host "  [OK] Granted '$PermissionRole' — Permission ID: $($granted.id)" -ForegroundColor Green

            Write-MigrationLog -LogFile $logFile -Level 'INFO' `
                -Message "Permission granted successfully" `
                -ClientId $clientId -ResourceId $siteId `
                -Extra @{
                    siteUrl      = $siteUrl
                    role         = $PermissionRole
                    permissionId = $granted.id
                    miAppId      = $MiAppId
                }

            $results.Granted++
        }
        else {
            Write-Host "  [WHATIF] Would grant '$PermissionRole' to MI on '$siteUrl'" -ForegroundColor Yellow
            $results.Skipped++
        }
    }
    catch {
        $errMsg = $_.ToString()
        Write-Host "  [FAIL] $errMsg" -ForegroundColor Red

        Write-MigrationLog -LogFile $logFile -Level 'ERROR' `
            -Message "Failed to grant permission" `
            -ClientId $clientId -ResourceId $siteId `
            -Extra @{ siteUrl = $siteUrl; error = $errMsg }

        $results.Failed++
    }

    # Polite inter-site delay — avoids burst throttling on large batches
    Start-Sleep -Seconds $ThrottleSleepSeconds
}

# ══════════════════════════════════════════════════════════════════════════════
# SUMMARY REPORT
# ══════════════════════════════════════════════════════════════════════════════
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Script A — Completed" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Total    : $($results.Total)"
Write-Host " Granted  : $($results.Granted)"  -ForegroundColor Green
Write-Host " Skipped  : $($results.Skipped)"  -ForegroundColor DarkGray
Write-Host " Failed   : $($results.Failed)"   -ForegroundColor $(if ($results.Failed -gt 0) { 'Red' } else { 'Green' })
Write-Host " Log file : $logFile"
Write-Host "========================================`n" -ForegroundColor Cyan

if ($results.Failed -gt 0) {
    Write-Warning "Some sites failed. Review the log file and re-run — the script is idempotent."
    exit 1
}

exit 0
