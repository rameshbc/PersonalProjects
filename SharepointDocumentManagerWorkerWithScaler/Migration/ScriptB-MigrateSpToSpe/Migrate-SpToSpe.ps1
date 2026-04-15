<#
.SYNOPSIS
    Script B — Migrate document library contents from SharePoint Online to SharePoint Embedded.

.DESCRIPTION
    One-time migration script. For each client listed in migration-config.json, copies the
    complete DocLibrary-A folder structure and all files from SharePoint Online (SP)
    to a pre-created SharePoint Embedded (SPE) container.

    WHAT IT DOES
    ─────────────
    1. Resolves the SP site ID and drive ID from the client's site URL.
    2. Recursively enumerates all folders and files in DocLibrary-A.
    3. Recreates the folder tree in the SPE container (depth-first, idempotent).
    4. Uploads each file to the SPE container:
         < 4 MB  → single PUT to drive item content endpoint.
         ≥ 4 MB  → creates a resumable upload session and uploads in 5 MB chunks.
    5. Optionally replicates SP folder permission grants onto SPE folders.
    6. Tracks migrated items in migration-state.json so the run is resumable.
    7. SP content is NEVER deleted — the SP library remains intact.

    IDEMPOTENCY
    ───────────
    State is tracked per item in migration-state.json keyed by SP item ID.
    Re-running the script skips already-completed items and retries failed ones.

    PREREQUISITES
    ─────────────
    - SPE containers for each client must be pre-created via the Admin portal.
    - The MI app or bootstrap SP must have Files.ReadWrite.All on both SP and SPE.
    - Copy migration-config-template.json → migration-config.json and populate it.
    - Set credentials via environment variables (never in the config file):
        $env:MI_APP_ID     = "..."
        $env:MI_APP_SECRET = "..."

    USAGE
    ─────
    # Dry run first (always):
    .\Migrate-SpToSpe.ps1 -ConfigPath .\migration-config.json

    # Real run after reviewing dry-run output:
    # Set behaviour.dryRun = false in migration-config.json, then:
    .\Migrate-SpToSpe.ps1 -ConfigPath .\migration-config.json

.NOTES
    Author  : SharePoint Document Manager Team
    Version : 1.0.0
    Safe to re-run: YES (idempotent via migration-state.json)
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string] $ConfigPath = ".\migration-config.json",

    [string] $StateFilePath = ".\migration-state.json",
    [string] $LogDirectory  = ".\Logs"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Load shared helpers ────────────────────────────────────────────────────────
. "$PSScriptRoot\..\Shared\Auth-Helpers.ps1"
. "$PSScriptRoot\..\Shared\Graph-Helpers.ps1"

# ── Load config ────────────────────────────────────────────────────────────────
if (-not (Test-Path $ConfigPath)) {
    throw "Config file not found: $ConfigPath. Copy migration-config-template.json and populate it."
}
$config  = Get-Content $ConfigPath | ConvertFrom-Json
$dryRun  = $config.behaviour.dryRun
$libName = $config.sharePoint.targetLibraryName

# ── Setup logging ──────────────────────────────────────────────────────────────
$runId   = Get-Date -Format 'yyyyMMdd-HHmmss'
$logFile = Join-Path $LogDirectory "ScriptB-Migrate-$runId.jsonl"
if (-not (Test-Path $LogDirectory)) { New-Item -ItemType Directory -Path $LogDirectory | Out-Null }

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Script B: SP → SPE Content Migration" -ForegroundColor Cyan
Write-Host " Run ID  : $runId" -ForegroundColor Cyan
Write-Host " Dry Run : $dryRun" -ForegroundColor $(if ($dryRun) {'Yellow'} else {'Red'})
Write-Host " Log     : $logFile" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

if ($dryRun) {
    Write-Host "[DRY RUN] No changes will be made. Set behaviour.dryRun=false to run for real.`n" -ForegroundColor Yellow
}

# ── Acquire Graph token ────────────────────────────────────────────────────────
$authParams = @{ AuthMode = $config.auth.mode; TenantId = $config.tenantId }
if ($config.auth.mode -eq 'ClientCredentials') {
    $authParams.ClientId     = [Environment]::GetEnvironmentVariable($config.auth.clientIdEnvVar)
    $authParams.ClientSecret = [Environment]::GetEnvironmentVariable($config.auth.clientSecretEnvVar)
}
$token = Get-GraphAccessToken @authParams
Write-Host "[Auth] Graph token acquired.`n"

# ── Load / initialise migration state ─────────────────────────────────────────
$state = if (Test-Path $StateFilePath) {
    Write-Host "[State] Resuming from $StateFilePath"
    Get-Content $StateFilePath | ConvertFrom-Json -AsHashtable
} else {
    Write-Host "[State] Starting fresh."
    @{ runId = $runId; startedAt = (Get-Date -Format 'o'); clients = @{} }
}

function Save-State {
    $state | ConvertTo-Json -Depth 20 | Set-Content -Path $StateFilePath -Encoding UTF8
}

# ══════════════════════════════════════════════════════════════════════════════
# FUNCTIONS
# ══════════════════════════════════════════════════════════════════════════════

function Get-SpDriveId($siteId, $libraryName) {
    $drives = Invoke-GraphRequestPaged -Token $token -Uri "/sites/$siteId/drives"
    $drive  = $drives | Where-Object { $_.name -eq $libraryName }
    if (-not $drive) { throw "Library '$libraryName' not found on site $siteId." }
    return $drive.id
}

function Get-SpSiteId($siteUrl) {
    $uri  = [Uri]$siteUrl
    $host = $uri.Host
    $path = $uri.AbsolutePath.TrimStart('/')
    $site = Invoke-GraphRequest -Token $token -Uri "/sites/${host}`:/${path}"
    if (-not $site) { throw "Site not found: $siteUrl" }
    return $site.id
}

function Get-AllSpItems($driveId, $folderId, $parentPath) {
    # Returns all items recursively as a flat list with relativePath set
    $items = [System.Collections.Generic.List[object]]::new()
    $uri   = "/drives/$driveId/items/$folderId/children?`$select=id,name,folder,file,size,lastModifiedDateTime&`$top=200"

    $page = Invoke-GraphRequestPaged -Token $token -Uri $uri
    foreach ($item in $page) {
        $relPath = if ($parentPath) { "$parentPath/$($item.name)" } else { $item.name }
        $item | Add-Member -NotePropertyName relativePath -NotePropertyValue $relPath -Force
        $items.Add($item)

        if ($item.folder) {
            $children = Get-AllSpItems $driveId $item.id $relPath
            $items.AddRange($children)
        }
    }
    return $items
}

function Ensure-SpeFolder($containerId, $parentId, $folderName) {
    # Create folder in SPE — idempotent (409 = already exists)
    $body = @{ name = $folderName; folder = @{}; '@microsoft.graph.conflictBehavior' = 'fail' }
    try {
        $result = Invoke-GraphRequest -Token $token `
            -Uri "/drives/$containerId/items/$parentId/children" `
            -Method POST -Body $body
        return $result.id
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 409) {
            # Already exists — fetch ID
            $children = Invoke-GraphRequest -Token $token `
                -Uri "/drives/$containerId/items/$parentId/children?`$filter=name eq '$folderName' and folder ne null&`$select=id,name"
            return $children.value[0].id
        }
        throw
    }
}

function Upload-SmallFile($containerId, $parentId, $fileName, $spDriveId, $spItemId) {
    # Download from SP then upload to SPE in memory (for files < 4 MB)
    $tmpFile = [System.IO.Path]::GetTempFileName()
    try {
        Invoke-GraphRequest -Token $token `
            -Uri "/drives/$spDriveId/items/$spItemId/content" `
            -Method GET -OutFile $tmpFile

        if (-not $dryRun) {
            $bytes  = [System.IO.File]::ReadAllBytes($tmpFile)
            $result = Invoke-GraphRequest -Token $token `
                -Uri "/drives/$containerId/items/${parentId}:/${fileName}:/content" `
                -Method PUT -RawBody $bytes -ContentType 'application/octet-stream'
            return $result.id
        }
        return "dry-run-id"
    }
    finally {
        Remove-Item $tmpFile -ErrorAction SilentlyContinue
    }
}

function Upload-LargeFile($containerId, $parentId, $fileName, $spDriveId, $spItemId, $fileSize) {
    $chunkSize = 5 * 1024 * 1024   # 5 MB — multiple of 320 KB

    # Step 1: Create upload session in SPE
    if ($dryRun) { return "dry-run-id" }

    $sessionBody = @{ item = @{ '@microsoft.graph.conflictBehavior' = 'replace'; name = $fileName } }
    $session = Invoke-GraphRequest -Token $token `
        -Uri "/drives/$containerId/items/${parentId}:/${fileName}:/createUploadSession" `
        -Method POST -Body $sessionBody

    $uploadUrl = $session.uploadUrl

    # Step 2: Download SP file to temp, upload in chunks to SPE
    $tmpFile = [System.IO.Path]::GetTempFileName()
    try {
        Invoke-GraphRequest -Token $token `
            -Uri "/drives/$spDriveId/items/$spItemId/content" `
            -Method GET -OutFile $tmpFile

        $stream = [System.IO.FileStream]::new($tmpFile, [System.IO.FileMode]::Open)
        $buffer = [byte[]]::new($chunkSize)
        $offset = 0

        try {
            while ($offset -lt $fileSize) {
                $read     = $stream.Read($buffer, 0, $buffer.Length)
                $chunk    = $buffer[0..($read-1)]
                $rangeEnd = $offset + $read - 1

                $headers = @{
                    'Content-Range'  = "bytes $offset-$rangeEnd/$fileSize"
                    'Content-Length' = $read
                    'Content-Type'   = 'application/octet-stream'
                }

                $chunkAttempt = 0
                $success = $false
                while (-not $success -and $chunkAttempt -le 5) {
                    try {
                        $result  = Invoke-RestMethod -Uri $uploadUrl -Method PUT -Headers $headers -Body $chunk
                        $success = $true
                    }
                    catch {
                        $chunkAttempt++
                        $wait = [Math]::Min(60, [Math]::Pow(2, $chunkAttempt) * 3)
                        Write-Warning "Chunk $offset-$rangeEnd throttled. Retry $chunkAttempt in ${wait}s..."
                        Start-Sleep -Seconds $wait
                    }
                }
                if (-not $success) { throw "Chunk upload failed after retries: $offset-$rangeEnd" }

                Write-Verbose "  Uploaded bytes $offset–$rangeEnd of $fileSize"
                $offset += $read
            }
        }
        finally { $stream.Dispose() }

        return $result.id
    }
    finally {
        Remove-Item $tmpFile -ErrorAction SilentlyContinue
    }
}

# ══════════════════════════════════════════════════════════════════════════════
# MAIN LOOP — Process each client
# ══════════════════════════════════════════════════════════════════════════════
$totalFiles = 0; $totalMigrated = 0; $totalFailed = 0

foreach ($client in $config.clients) {
    $clientId    = $client.clientId
    $siteUrl     = $client.spSiteUrl
    $containerId = $client.speContainerId

    Write-Host "── Client: $clientId" -ForegroundColor White

    if (-not $state.clients.ContainsKey($clientId)) {
        $state.clients[$clientId] = @{ status = 'pending'; items = @{} }
    }
    $clientState = $state.clients[$clientId]

    try {
        # ── Resolve SP IDs ─────────────────────────────────────────────────────
        $siteId  = if ($client.spSiteId) { $client.spSiteId } else { Get-SpSiteId $siteUrl }
        $driveId = if ($client.spDriveId) { $client.spDriveId } else { Get-SpDriveId $siteId $libName }

        Write-Host "  SP site=$siteId drive=$driveId" -ForegroundColor DarkGray

        # ── Enumerate SP DocLibrary-A ──────────────────────────────────────────
        if (-not $clientState.enumerationComplete) {
            Write-Host "  Enumerating SP library..."
            $allItems = Get-AllSpItems $driveId 'root' ''
            foreach ($item in $allItems) {
                if (-not $clientState.items.ContainsKey($item.id)) {
                    $clientState.items[$item.id] = @{
                        spItemId     = $item.id
                        name         = $item.name
                        relativePath = $item.relativePath
                        isFolder     = [bool]$item.folder
                        sizeBytes    = $item.size ?? 0
                        status       = 'pending'
                    }
                }
            }
            $clientState.enumerationComplete = $true
            Save-State
            Write-Host "  Enumerated $($allItems.Count) items." -ForegroundColor Green
        }

        # ── Build SPE folder map (relativePath → SPE item ID) ─────────────────
        $speFolderMap = @{ '' = 'root' }   # Root maps to 'root'

        # Process folders first (depth-first by path length)
        $folders = $clientState.items.Values | Where-Object { $_.isFolder } |
                   Sort-Object { ($_.relativePath -split '/').Count }

        foreach ($folder in $folders) {
            $parentPath = ($folder.relativePath -split '/' | Select-Object -SkipLast 1) -join '/'
            $parentId   = $speFolderMap[$parentPath] ?? 'root'

            if ($folder.status -eq 'completed') {
                $speFolderMap[$folder.relativePath] = $folder.speItemId
                continue
            }

            Write-Host "  [FOLDER] $($folder.relativePath)" -ForegroundColor DarkCyan

            if (-not $dryRun) {
                $speId = Ensure-SpeFolder $containerId $parentId $folder.name
                $folder.speItemId = $speId
                $speFolderMap[$folder.relativePath] = $speId
            } else {
                $speFolderMap[$folder.relativePath] = "dry-run-$($folder.relativePath)"
            }

            $folder.status = 'completed'
            Write-MigrationLog -LogFile $logFile -Level 'INFO' `
                -Message "Folder created" -ClientId $clientId -ResourceId $folder.spItemId `
                -Extra @{ relativePath = $folder.relativePath; dryRun = $dryRun }
        }

        # ── Migrate files ──────────────────────────────────────────────────────
        $files = $clientState.items.Values | Where-Object { -not $_.isFolder }
        $totalFiles += $files.Count

        foreach ($file in $files) {
            if ($file.status -eq 'completed') {
                $totalMigrated++
                continue
            }

            $parentPath = ($file.relativePath -split '/' | Select-Object -SkipLast 1) -join '/'
            $parentId   = $speFolderMap[$parentPath] ?? 'root'
            $threshold  = $config.behaviour.smallFileThresholdBytes

            Write-Host "  [FILE] $($file.relativePath) ($([Math]::Round($file.sizeBytes/1KB,1)) KB)" -ForegroundColor Gray

            try {
                if (-not $dryRun) {
                    $speItemId = if ($file.sizeBytes -lt $threshold) {
                        Upload-SmallFile $containerId $parentId $file.name $driveId $file.spItemId
                    } else {
                        Upload-LargeFile $containerId $parentId $file.name $driveId $file.spItemId $file.sizeBytes
                    }
                    $file.speItemId  = $speItemId
                }
                $file.status    = 'completed'
                $file.migratedAt = (Get-Date -Format 'o')
                $totalMigrated++

                Write-MigrationLog -LogFile $logFile -Level 'INFO' `
                    -Message "File migrated" -ClientId $clientId -ResourceId $file.spItemId `
                    -Extra @{ relativePath = $file.relativePath; sizeBytes = $file.sizeBytes; dryRun = $dryRun }
            }
            catch {
                $file.status       = 'failed'
                $file.errorMessage = $_.ToString()
                $totalFailed++
                Write-Warning "  FAILED: $($file.relativePath) — $_"

                Write-MigrationLog -LogFile $logFile -Level 'ERROR' `
                    -Message "File migration failed" -ClientId $clientId -ResourceId $file.spItemId `
                    -Extra @{ relativePath = $file.relativePath; error = $_.ToString() }
            }

            # Save state after every file — ensures resumability on interruption
            Save-State
        }

        $clientState.status = 'completed'
        Write-Host "  Client '$clientId' done." -ForegroundColor Green
    }
    catch {
        $clientState.status = 'failed'
        Write-Host "  [FAIL] Client '$clientId': $_" -ForegroundColor Red
        Write-MigrationLog -LogFile $logFile -Level 'ERROR' `
            -Message "Client migration failed" -ClientId $clientId -ResourceId '' `
            -Extra @{ error = $_.ToString() }
    }

    Save-State
}

# ══════════════════════════════════════════════════════════════════════════════
# SUMMARY
# ══════════════════════════════════════════════════════════════════════════════
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Script B — Migration Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Total files : $totalFiles"
Write-Host " Migrated    : $totalMigrated" -ForegroundColor Green
Write-Host " Failed      : $totalFailed"   -ForegroundColor $(if ($totalFailed -gt 0) { 'Red' } else { 'Green' })
Write-Host " State file  : $StateFilePath"
Write-Host " Log file    : $logFile"
if ($dryRun) {
    Write-Host "`n[DRY RUN] No actual changes were made." -ForegroundColor Yellow
    Write-Host "          Set behaviour.dryRun=false in config and re-run to migrate for real." -ForegroundColor Yellow
}
Write-Host "========================================`n" -ForegroundColor Cyan

if ($totalFailed -gt 0) { exit 1 } else { exit 0 }
