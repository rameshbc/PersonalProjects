<#
.SYNOPSIS
    Deploys Office Scripts to service account OneDrive ("My Scripts") via Microsoft Graph API.

.DESCRIPTION
    Office Scripts stored in a service account's OneDrive are available to every Power Automate
    flow owned by that account, regardless of which SharePoint site or workbook the flow targets.
    Deploy once per service account — NOT once per workbook.

    The script uploads each .ts file as an .osts (JSON) file to:
        {service-account-OneDrive}/Documents/Office Scripts/{ScriptName}.osts

    In Power Automate, open the "Run script" action, select the workbook, then pick the script
    from the "My scripts" tab — the scripts deployed here will appear in that list.

    This is an upsert: if the .osts file already exists it is overwritten in place.

.PARAMETER TenantId
    Azure AD / Entra ID tenant ID (GUID or verified domain, e.g. "contoso.com").

.PARAMETER ClientId
    App registration client ID for OfficeScriptWorkflowPOC-ScriptDeployer.
    Required Application permission (admin consented): Files.ReadWrite.All.
    See docs/azure-ad-setup.md for setup instructions.

.PARAMETER ClientSecret
    Client secret for the app registration, as a SecureString.
    If omitted, the script falls back to device code flow (interactive browser sign-in).

.PARAMETER ServiceAccountUpns
    One or more M365 user principal names (UPNs) whose OneDrive will receive the scripts.
    Provide the UPN of EVERY service account that owns Power Automate flows:
      - The primary service account (flows in WorkbookRegistry)
      - Each additional account in FlowAccountPool (if configured)
    Example: -ServiceAccountUpns "svc-os-01@contoso.onmicrosoft.com","svc-os-02@contoso.onmicrosoft.com"

.PARAMETER ScriptsDir
    Directory containing .ts Office Script source files.
    Defaults to office-scripts/ relative to the repository root (parent of scripts/).

.PARAMETER WhatIf
    Print planned actions without making any Graph API calls.

.EXAMPLE
    # Interactive sign-in (device code) — single service account
    pwsh scripts/Deploy-OfficeScripts.ps1 `
      -TenantId YOUR_TENANT_ID `
      -ClientId YOUR_DEPLOYER_APP_ID `
      -ServiceAccountUpns "svc-officescript@contoso.onmicrosoft.com"

.EXAMPLE
    # CI/CD (client credentials) — multiple service accounts
    $secret = ConvertTo-SecureString $env:GRAPH_CLIENT_SECRET -AsPlainText -Force
    pwsh scripts/Deploy-OfficeScripts.ps1 `
      -TenantId  $env:TENANT_ID `
      -ClientId  $env:CLIENT_ID `
      -ClientSecret $secret `
      -ServiceAccountUpns ($env:SERVICE_ACCOUNT_UPNS -split ',')

.EXAMPLE
    # Dry run — prints planned actions, makes no Graph calls
    pwsh scripts/Deploy-OfficeScripts.ps1 `
      -TenantId x -ClientId y `
      -ServiceAccountUpns "svc@contoso.onmicrosoft.com" `
      -WhatIf
#>

param (
    [Parameter(Mandatory)]
    [string] $TenantId,

    [Parameter(Mandatory)]
    [string] $ClientId,

    [Parameter()]
    [SecureString] $ClientSecret,

    [Parameter(Mandatory)]
    [string[]] $ServiceAccountUpns,

    [Parameter()]
    [string] $ScriptsDir,

    [switch] $WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── .osts file format ─────────────────────────────────────────────────────────
#
# Office Script personal files use the .osts extension. They are JSON documents
# stored in the service account's OneDrive at:
#   Documents/Office Scripts/{ScriptName}.osts
#
# Schema (all three fields are required by Excel):
#   scriptVersion : "1.0"   — file format version, always "1.0"
#   apiVersion    : "1.1"   — ExcelScript API version; "1.1" covers all current APIs
#   script        : string  — the full TypeScript source (the function main(...) body)
#
# Graph upload API (upsert — creates or overwrites):
#   PUT /v1.0/drives/{driveId}/root:/Documents/Office%20Scripts/{name}.osts:/content
#   Content-Type: application/json
#   Body: the .osts JSON string
#
# "Office Scripts" has a space — this MUST be percent-encoded as "Office%20Scripts"
# in all Graph path-based URL segments, otherwise the request returns 400 or 404.
#
# ─────────────────────────────────────────────────────────────────────────────

# URL-encoded path used in all Graph calls (space → %20)
$GRAPH_SCRIPTS_PATH = 'Documents/Office%20Scripts'

$OSTS_SCRIPT_VERSION = '1.0'
$OSTS_API_VERSION    = '1.1'

#region ── Helpers ────────────────────────────────────────────────────────────

function Resolve-RepoRoot {
    # Deploy-OfficeScripts.ps1 lives in scripts/ which is one level below the repo root.
    $scriptDir = Split-Path -Parent $PSCommandPath
    return Split-Path -Parent $scriptDir
}

function Get-AccessToken {
    param (
        [string]       $TenantId,
        [string]       $ClientId,
        [SecureString] $ClientSecret   # $null → device code flow
    )

    $tokenUrl = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
    $scope    = 'https://graph.microsoft.com/.default'

    if ($ClientSecret) {
        # Client credentials — non-interactive, suitable for CI/CD.
        # ConvertFrom-SecureString -AsPlainText requires PowerShell 7+.
        $plain = ConvertFrom-SecureString $ClientSecret -AsPlainText
        Write-Host '[Auth] Acquiring token via client credentials...'
        $r = Invoke-RestMethod -Method Post -Uri $tokenUrl -ContentType 'application/x-www-form-urlencoded' `
            -Body @{
                grant_type    = 'client_credentials'
                client_id     = $ClientId
                client_secret = $plain
                scope         = $scope
            }
        return $r.access_token
    }

    # Device code — interactive, opens browser for user to authenticate.
    $dcUrl = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/devicecode"
    $dc    = Invoke-RestMethod -Method Post -Uri $dcUrl -ContentType 'application/x-www-form-urlencoded' `
        -Body @{ client_id = $ClientId; scope = $scope }

    Write-Host ''
    Write-Host $dc.message    # prints the "Go to https://... and enter code XXXX-XXXX" message
    Write-Host ''

    $deadline = (Get-Date).AddSeconds([int]$dc.expires_in)
    $interval = [int]$dc.interval

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds $interval
        try {
            $t = Invoke-RestMethod -Method Post -Uri $tokenUrl -ContentType 'application/x-www-form-urlencoded' `
                -Body @{
                    grant_type  = 'urn:ietf:params:oauth:grant-type:device_code'
                    client_id   = $ClientId
                    device_code = $dc.device_code
                }
            return $t.access_token
        }
        catch {
            $e = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            switch ($e.error) {
                'authorization_pending' { continue }
                'authorization_declined' { throw 'Device code authorization was declined by the user.' }
                'expired_token'          { throw 'Device code expired. Re-run the script to get a new code.' }
                default                  { throw }
            }
        }
    }
    throw 'Device code flow timed out without user authentication.'
}

function Invoke-GraphGet {
    # Returns the parsed response object, or $null on any error (non-terminating).
    param (
        [string]    $Uri,
        [hashtable] $Headers
    )
    try {
        return Invoke-RestMethod -Method GET -Uri $Uri -Headers $Headers
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        $body   = $_.ErrorDetails.Message
        Write-Warning "  Graph GET $Uri → HTTP $status : $body"
        return $null
    }
}

function Invoke-GraphPost {
    # Posts a JSON body. Returns the parsed response, or $null on error.
    param (
        [string]    $Uri,
        [hashtable] $Headers,
        [string]    $JsonBody
    )
    try {
        return Invoke-RestMethod -Method POST -Uri $Uri -Headers $Headers `
            -Body $JsonBody -ContentType 'application/json'
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        $body   = $_.ErrorDetails.Message
        Write-Warning "  Graph POST $Uri → HTTP $status : $body"
        return $null
    }
}

function Invoke-GraphPut {
    # Uploads raw string content to a OneDrive item path (creates or overwrites).
    # Returns the parsed response (the created/updated DriveItem), or $null on error.
    param (
        [string]    $Uri,
        [hashtable] $Headers,
        [string]    $RawBody,
        [string]    $ContentType = 'application/json'
    )
    try {
        return Invoke-RestMethod -Method PUT -Uri $Uri -Headers $Headers `
            -Body $RawBody -ContentType $ContentType
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        $body   = $_.ErrorDetails.Message
        Write-Warning "  Graph PUT $Uri → HTTP $status : $body"
        return $null
    }
}

function Build-OstsJson {
    # Wraps TypeScript source in the .osts JSON envelope required by Excel.
    param ([string] $TypeScript)
    return ([ordered]@{
        scriptVersion = $OSTS_SCRIPT_VERSION
        apiVersion    = $OSTS_API_VERSION
        script        = $TypeScript
    } | ConvertTo-Json -Depth 3 -Compress)
}

function Ensure-OstsFolder {
    # Verifies that Documents/Office Scripts exists in the given drive.
    # Creates the "Office Scripts" subfolder inside Documents if missing.
    # Returns $true if the folder is confirmed to exist, $false on failure.
    param (
        [string]    $DriveId,
        [hashtable] $Headers
    )

    $checkUri = "https://graph.microsoft.com/v1.0/drives/${DriveId}/root:/${GRAPH_SCRIPTS_PATH}"
    $folder   = Invoke-GraphGet -Uri $checkUri -Headers $Headers

    if ($folder) {
        Write-Host "  [Folder] 'Documents/Office Scripts' exists."
        return $true
    }

    # Folder not found — create "Office Scripts" inside Documents.
    # Using conflictBehavior "fail" so a 409 response means it already exists
    # (race condition between check and create is handled safely).
    Write-Host "  [Folder] 'Documents/Office Scripts' not found — creating..."

    $createUri  = "https://graph.microsoft.com/v1.0/drives/${DriveId}/root:/Documents:/children"
    $createBody = '{"name":"Office Scripts","folder":{},"@microsoft.graph.conflictBehavior":"fail"}'

    $created = Invoke-GraphPost -Uri $createUri -Headers $Headers -JsonBody $createBody

    if ($created) {
        Write-Host "  [Folder] Created 'Documents/Office Scripts'."
        return $true
    }

    # Invoke-GraphPost returns $null on any error, including 409 Conflict (folder exists).
    # Re-check — a 409 means the folder appeared between our GET and POST.
    $recheck = Invoke-GraphGet -Uri $checkUri -Headers $Headers
    if ($recheck) {
        Write-Host "  [Folder] 'Documents/Office Scripts' confirmed (created concurrently)."
        return $true
    }

    Write-Warning "  [Folder] Could not create or confirm 'Documents/Office Scripts'. Uploads will likely fail."
    return $false
}

#endregion

#region ── Resolve paths ──────────────────────────────────────────────────────

$repoRoot = Resolve-RepoRoot

if (-not $ScriptsDir) {
    $ScriptsDir = Join-Path $repoRoot 'office-scripts'
}

Write-Host ''
Write-Host "[Config] Scripts directory   : $ScriptsDir"
Write-Host "[Config] Service account(s)  : $($ServiceAccountUpns -join ', ')"
if ($WhatIf) {
    Write-Host '[WhatIf] Dry-run mode — no Graph calls will be made.'
}

#endregion

#region ── Load .ts script files ─────────────────────────────────────────────

if (-not (Test-Path $ScriptsDir)) {
    throw "Scripts directory not found: $ScriptsDir"
}

$scriptFiles = Get-ChildItem -Path $ScriptsDir -Filter '*.ts' | Sort-Object Name

if ($scriptFiles.Count -eq 0) {
    throw "No .ts files found in: $ScriptsDir"
}

Write-Host "[Scripts] $($scriptFiles.Count) file(s) to deploy:"
$scriptFiles | ForEach-Object { Write-Host "  - $($_.Name)" }

#endregion

#region ── Authenticate ───────────────────────────────────────────────────────

if (-not $WhatIf) {
    $token   = Get-AccessToken -TenantId $TenantId -ClientId $ClientId -ClientSecret $ClientSecret
    $headers = @{ Authorization = "Bearer $token" }
    Write-Host '[Auth] Token acquired.'
}
else {
    $headers = @{ Authorization = 'Bearer WHATIF_TOKEN' }
}

#endregion

#region ── Deployment loop ────────────────────────────────────────────────────

$summary = [PSCustomObject]@{
    AccountsProcessed = 0
    ScriptsUploaded   = 0   # successful PUT upserts
    ScriptsSkipped    = 0   # WhatIf only
    Errors            = 0
}

foreach ($upn in $ServiceAccountUpns) {

    Write-Host ''
    Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━'
    Write-Host "Service account: $upn"
    Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━'

    #region — Resolve OneDrive drive ID

    $driveId = $null

    if ($WhatIf) {
        Write-Host "  [WhatIf] Would resolve OneDrive for $upn"
        Write-Host "  [WhatIf] Would ensure 'Documents/Office Scripts' folder exists"
        foreach ($file in $scriptFiles) {
            $scriptName     = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            $encodedOtsName = [Uri]::EscapeDataString("$scriptName.osts")
            $sizeKb         = [Math]::Round($file.Length / 1024, 1)
            Write-Host "  [WhatIf] Would PUT Documents/Office Scripts/$scriptName.osts ($sizeKb KB)"
            $summary.ScriptsSkipped++
        }
        $summary.AccountsProcessed++
        continue
    }

    Write-Host "  [Graph] GET /v1.0/users/$upn/drive"
    $drive = Invoke-GraphGet -Uri "https://graph.microsoft.com/v1.0/users/$upn/drive" -Headers $headers

    if (-not $drive) {
        Write-Warning "  [Skip] Cannot resolve OneDrive for '$upn'."
        Write-Warning "         Causes: incorrect UPN, account has no OneDrive licence, or"
        Write-Warning "         Files.ReadWrite.All admin consent not yet granted for ClientId=$ClientId."
        $summary.Errors++
        continue
    }

    $driveId = $drive.id
    Write-Host "  [Resolved] driveId = $driveId"

    #endregion

    #region — Ensure Documents/Office Scripts folder exists

    $folderOk = Ensure-OstsFolder -DriveId $driveId -Headers $headers

    if (-not $folderOk) {
        # Warning already printed inside Ensure-OstsFolder; attempt uploads anyway
        # (PUT may still succeed if the folder was created by something else).
        Write-Warning "  Proceeding with uploads despite folder check failure."
    }

    #endregion

    #region — Upload each .ts file as an .osts file (upsert)

    foreach ($file in $scriptFiles) {
        $scriptName     = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        # Percent-encode the filename in case it contains characters that need encoding.
        # For the standard script names (e.g. InsertRangeScript) this is a no-op.
        $encodedOtsName = [Uri]::EscapeDataString("$scriptName.osts")

        $uploadUri = "https://graph.microsoft.com/v1.0/drives/${driveId}/root:/${GRAPH_SCRIPTS_PATH}/${encodedOtsName}:/content"

        $tsContent = Get-Content -Raw $file.FullName
        $ostsJson  = Build-OstsJson -TypeScript $tsContent

        Write-Host "  [Upload] PUT Documents/Office Scripts/$scriptName.osts ..."
        $result = Invoke-GraphPut -Uri $uploadUri -Headers $headers -RawBody $ostsJson

        if ($result) {
            Write-Host "  [OK]    '$scriptName.osts' uploaded (OneDrive item id=$($result.id))."
            $summary.ScriptsUploaded++
        }
        else {
            Write-Warning "  [Error] Failed to upload '$scriptName.osts'. See warning above."
            $summary.Errors++
        }
    }

    #endregion

    $summary.AccountsProcessed++
}

#endregion

#region ── Summary ────────────────────────────────────────────────────────────

Write-Host ''
Write-Host '════════════════════════════════════════════════════════════════'
Write-Host '  Deployment Summary'
Write-Host '════════════════════════════════════════════════════════════════'
Write-Host "  Service accounts processed : $($summary.AccountsProcessed) of $($ServiceAccountUpns.Count)"
Write-Host "  Scripts uploaded (upserted): $($summary.ScriptsUploaded)"
if ($WhatIf) {
    Write-Host "  Scripts skipped (WhatIf)   : $($summary.ScriptsSkipped)"
}
Write-Host "  Errors                     : $($summary.Errors)"
Write-Host '════════════════════════════════════════════════════════════════'

if ($WhatIf) {
    Write-Host ''
    Write-Host '[WhatIf] No changes were made. Remove -WhatIf to deploy.'
}
elseif ($summary.Errors -gt 0) {
    Write-Warning "Completed with $($summary.Errors) error(s). Review warnings above."
    exit 1
}
else {
    Write-Host 'Deployment completed successfully.'
    Write-Host ''
    Write-Host 'Next step: in each Power Automate flow, open the "Run script" action,'
    Write-Host '  select the target workbook, then choose the script from the "My scripts" tab.'
    Write-Host '  Script names match filenames without the .ts extension (e.g. InsertRangeScript).'
}

#endregion
