<#
.SYNOPSIS
    Microsoft Graph API helpers — throttling-aware REST wrapper and paging support.

.DESCRIPTION
    Provides:
      - Invoke-GraphRequest        : Single Graph call with automatic retry on 429/503.
      - Invoke-GraphRequestPaged   : Follows @odata.nextLink automatically — returns all pages.
      - New-GraphUploadSession     : Creates a resumable upload session for large files.
      - Send-GraphLargeFileChunked : Uploads a file in 5 MB chunks via an upload session.
      - Invoke-GraphBatch          : Sends up to 20 requests in a single $batch call.

.NOTES
    Throttling strategy:
      - On HTTP 429 or 503, reads the Retry-After response header.
      - Waits (Retry-After + random jitter 1–5 s) before retrying.
      - After MaxRetries exhausted, throws so the caller can log and continue.

    Chunk size for large uploads must be a multiple of 320 KB.
    This file uses 5 MB (5,242,880 bytes) as the default chunk size.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ──────────────────────────────────────────────────────────────────────────────
# Constants
# ──────────────────────────────────────────────────────────────────────────────
$script:GraphBaseUrl  = 'https://graph.microsoft.com/v1.0'
$script:ChunkSizeBytes = 5 * 1024 * 1024   # 5 MB — must be multiple of 320 KB

# ──────────────────────────────────────────────────────────────────────────────
# Private: Build-GraphHeaders
# ──────────────────────────────────────────────────────────────────────────────
function Build-GraphHeaders {
    param ([string] $Token, [string] $ContentType = 'application/json')
    return @{
        Authorization  = "Bearer $Token"
        'Content-Type' = $ContentType
        Accept         = 'application/json'
        # ConsistencyLevel required for advanced Graph queries ($count, $search on directory objects)
        ConsistencyLevel = 'eventual'
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# Public: Invoke-GraphRequest
# ──────────────────────────────────────────────────────────────────────────────
function Invoke-GraphRequest {
    <#
    .SYNOPSIS
        Makes a single Microsoft Graph REST call with retry-on-throttle logic.

    .PARAMETER Token
        Bearer access token (string) from Get-GraphAccessToken.

    .PARAMETER Uri
        Full Graph URL or a relative path (e.g. /sites/{id}/drives).
        Relative paths are prefixed with https://graph.microsoft.com/v1.0.

    .PARAMETER Method
        HTTP verb: GET | POST | PUT | PATCH | DELETE.

    .PARAMETER Body
        PowerShell hashtable — serialised to JSON automatically.
        For binary uploads use -RawBody with -ContentType 'application/octet-stream'.

    .PARAMETER RawBody
        Raw byte array for binary requests (file upload content).

    .PARAMETER ContentType
        Defaults to 'application/json'. Override to 'application/octet-stream' for binary.

    .PARAMETER MaxRetries
        How many times to retry after a 429/503. Default: 7.

    .PARAMETER OutFile
        Path to save binary response (e.g. downloaded file content).

    .EXAMPLE
        $drives = Invoke-GraphRequest -Token $token -Uri "/sites/$siteId/drives"
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)] [string] $Token,
        [Parameter(Mandatory)] [string] $Uri,
        [string]   $Method      = 'GET',
        [object]   $Body        = $null,
        [byte[]]   $RawBody     = $null,
        [string]   $ContentType = 'application/json',
        [int]      $MaxRetries  = 7,
        [string]   $OutFile     = $null
    )

    # Normalise relative paths
    if ($Uri -notlike 'https://*') {
        $Uri = "$script:GraphBaseUrl$Uri"
    }

    $headers = Build-GraphHeaders -Token $Token -ContentType $ContentType
    $attempt = 0

    while ($true) {
        try {
            $invokeParams = @{
                Uri         = $Uri
                Method      = $Method
                Headers     = $headers
                ErrorAction = 'Stop'
            }

            if ($RawBody) {
                $invokeParams.Body = $RawBody
            }
            elseif ($Body -and $Method -ne 'GET') {
                $invokeParams.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
            }

            if ($OutFile) {
                $invokeParams.OutFile = $OutFile
                Invoke-RestMethod @invokeParams | Out-Null
                return $null   # Binary download — no JSON response object
            }

            return Invoke-RestMethod @invokeParams
        }
        catch {
            $statusCode = [int]$_.Exception.Response.StatusCode

            # ── Throttling: back off then retry ─────────────────────────────
            if ($statusCode -in 429, 503) {
                $attempt++
                if ($attempt -gt $MaxRetries) {
                    throw "Graph request to '$Uri' throttled $MaxRetries times. Giving up. Last error: $_"
                }

                # Prefer Retry-After header; default to exponential back-off
                $retryAfterHeader = $_.Exception.Response.Headers['Retry-After']
                $waitSeconds = if ($retryAfterHeader) {
                    [int]$retryAfterHeader
                } else {
                    [Math]::Min(120, [Math]::Pow(2, $attempt) * 5)
                }
                $jitter = Get-Random -Minimum 1 -Maximum 6

                Write-Warning "[Graph] HTTP $statusCode — waiting $($waitSeconds + $jitter)s before retry $attempt/$MaxRetries for: $Uri"
                Start-Sleep -Seconds ($waitSeconds + $jitter)
                continue
            }

            # ── 404 — caller decides how to handle missing resources ─────────
            if ($statusCode -eq 404) {
                return $null
            }

            # ── All other errors — surface immediately ───────────────────────
            $errBody = $null
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                $reader = [System.IO.StreamReader]::new($stream)
                $errBody = $reader.ReadToEnd()
            } catch {}

            throw "[Graph] HTTP $statusCode on $Method $Uri`nResponse: $errBody`nException: $_"
        }
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# Public: Invoke-GraphRequestPaged
# ──────────────────────────────────────────────────────────────────────────────
function Invoke-GraphRequestPaged {
    <#
    .SYNOPSIS
        Calls a Graph list endpoint and follows all @odata.nextLink pages.
        Returns a flat array of all items across all pages.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)] [string] $Token,
        [Parameter(Mandatory)] [string] $Uri,
        [int] $MaxRetries = 7
    )

    $allItems = [System.Collections.Generic.List[object]]::new()
    $nextUri  = $Uri

    while ($nextUri) {
        Write-Verbose "[Graph Paging] GET $nextUri"
        $page    = Invoke-GraphRequest -Token $Token -Uri $nextUri -MaxRetries $MaxRetries
        $nextUri = $null

        if ($page -and $page.value) {
            $allItems.AddRange([object[]]$page.value)
        }

        # Follow nextLink if present
        if ($page.'@odata.nextLink') {
            $nextUri = $page.'@odata.nextLink'
        }
    }

    return $allItems.ToArray()
}

# ──────────────────────────────────────────────────────────────────────────────
# Public: New-GraphUploadSession
# ──────────────────────────────────────────────────────────────────────────────
function New-GraphUploadSession {
    <#
    .SYNOPSIS
        Creates a resumable upload session for files larger than 4 MB.
        Returns the uploadUrl to use with Send-GraphLargeFileChunked.

    .PARAMETER DriveId
        The drive (or SPE container drive) ID.

    .PARAMETER ParentItemId
        The ID of the destination folder item.

    .PARAMETER FileName
        Target file name (with extension).
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)] [string] $Token,
        [Parameter(Mandatory)] [string] $DriveId,
        [Parameter(Mandatory)] [string] $ParentItemId,
        [Parameter(Mandatory)] [string] $FileName
    )

    $uri  = "/drives/$DriveId/items/$ParentItemId`:/$FileName`:/createUploadSession"
    $body = @{
        item = @{
            '@microsoft.graph.conflictBehavior' = 'replace'
            name                                = $FileName
        }
    }

    $session = Invoke-GraphRequest -Token $Token -Uri $uri -Method POST -Body $body
    return $session.uploadUrl
}

# ──────────────────────────────────────────────────────────────────────────────
# Public: Send-GraphLargeFileChunked
# ──────────────────────────────────────────────────────────────────────────────
function Send-GraphLargeFileChunked {
    <#
    .SYNOPSIS
        Uploads a local file to Graph in 5 MB chunks using a pre-created upload session URL.
        Handles partial failures per chunk with per-chunk retry.

    .PARAMETER UploadUrl
        The uploadUrl returned by New-GraphUploadSession.

    .PARAMETER LocalFilePath
        Full path of the file to upload.

    .PARAMETER MaxChunkRetries
        How many times to retry a failed chunk before aborting. Default: 5.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)] [string] $Token,
        [Parameter(Mandatory)] [string] $UploadUrl,
        [Parameter(Mandatory)] [string] $LocalFilePath,
        [int] $MaxChunkRetries = 5
    )

    $fileInfo  = Get-Item $LocalFilePath
    $totalSize = $fileInfo.Length
    $stream    = [System.IO.FileStream]::new($LocalFilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read)

    try {
        $offset = 0
        $buffer = [byte[]]::new($script:ChunkSizeBytes)

        while ($offset -lt $totalSize) {
            $read      = $stream.Read($buffer, 0, $buffer.Length)
            $chunkData = $buffer[0..($read - 1)]
            $rangeEnd  = $offset + $read - 1

            $chunkHeaders = @{
                'Content-Range'  = "bytes $offset-$rangeEnd/$totalSize"
                'Content-Length' = $read
                'Content-Type'   = 'application/octet-stream'
            }

            # Retry each chunk independently
            $chunkAttempt = 0
            $chunkSuccess = $false

            while (-not $chunkSuccess -and $chunkAttempt -le $MaxChunkRetries) {
                try {
                    # Upload sessions use PUT directly to the uploadUrl (no Auth header needed)
                    $result = Invoke-RestMethod `
                        -Uri         $UploadUrl `
                        -Method      PUT `
                        -Headers     $chunkHeaders `
                        -Body        $chunkData `
                        -ErrorAction Stop
                    $chunkSuccess = $true
                }
                catch {
                    $statusCode = [int]$_.Exception.Response.StatusCode
                    $chunkAttempt++

                    if ($statusCode -in 429, 503 -and $chunkAttempt -le $MaxChunkRetries) {
                        $wait = [Math]::Min(60, [Math]::Pow(2, $chunkAttempt) * 3)
                        Write-Warning "[Upload] Chunk $offset-$rangeEnd throttled. Retrying in ${wait}s..."
                        Start-Sleep -Seconds $wait
                    }
                    else {
                        throw "[Upload] Chunk $offset-$rangeEnd failed after $MaxChunkRetries retries: $_"
                    }
                }
            }

            Write-Verbose "[Upload] Uploaded bytes $offset–$rangeEnd of $totalSize"
            $offset += $read
        }

        Write-Verbose "[Upload] Large file upload complete: $LocalFilePath"
        return $result   # Final chunk response contains the created DriveItem
    }
    finally {
        $stream.Dispose()
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# Public: Invoke-GraphBatch
# ──────────────────────────────────────────────────────────────────────────────
function Invoke-GraphBatch {
    <#
    .SYNOPSIS
        Sends up to 20 Graph requests in a single $batch HTTP call.
        Automatically retries throttled requests within the batch response.

    .PARAMETER Requests
        Array of hashtables. Each must have: id (string), method, url.
        Optional: body (hashtable), headers (hashtable).

    .OUTPUTS
        Array of response objects from the batch — each has id, status, body.

    .EXAMPLE
        $reqs = @(
            @{ id = '1'; method = 'GET'; url = "/sites/$siteId" },
            @{ id = '2'; method = 'GET'; url = "/drives/$driveId/root/children" }
        )
        $results = Invoke-GraphBatch -Token $token -Requests $reqs
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)] [string]   $Token,
        [Parameter(Mandatory)] [object[]] $Requests,
        [int] $MaxRetries = 5
    )

    if ($Requests.Count -gt 20) {
        throw '[Batch] Graph $batch supports a maximum of 20 requests per call.'
    }

    $batchUri  = 'https://graph.microsoft.com/v1.0/$batch'
    $batchBody = @{ requests = $Requests }

    $attempt = 0
    while ($true) {
        $response = Invoke-GraphRequest -Token $Token -Uri $batchUri -Method POST -Body $batchBody

        # Collect throttled sub-requests and retry them
        $throttled = $response.responses | Where-Object { $_.status -in 429, 503 }

        if (-not $throttled -or $attempt -ge $MaxRetries) {
            return $response.responses
        }

        $attempt++
        $maxWait = ($throttled | ForEach-Object {
            [int]($_.headers.'Retry-After' ?? 30)
        } | Measure-Object -Maximum).Maximum

        Write-Warning "[Batch] $($throttled.Count) sub-request(s) throttled. Waiting ${maxWait}s before partial retry (attempt $attempt)..."
        Start-Sleep -Seconds ($maxWait + (Get-Random -Minimum 1 -Maximum 5))

        # Retry only the throttled sub-requests
        $Requests = $throttled | ForEach-Object {
            $Requests | Where-Object { $_.id -eq $_.id }
        }
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# Public: Write-MigrationLog
# ──────────────────────────────────────────────────────────────────────────────
function Write-MigrationLog {
    <#
    .SYNOPSIS
        Appends a structured log entry (JSON line) to the migration log file.
        Each line is independently parseable — no wrapping array needed.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)] [string] $LogFile,
        [Parameter(Mandatory)] [string] $Level,        # INFO | WARN | ERROR
        [Parameter(Mandatory)] [string] $Message,
        [string] $ClientId   = '',
        [string] $ResourceId = '',
        [hashtable] $Extra   = @{}
    )

    $entry = @{
        timestamp  = (Get-Date -Format 'o')
        level      = $Level
        message    = $Message
        clientId   = $ClientId
        resourceId = $ResourceId
    } + $Extra

    $entry | ConvertTo-Json -Compress | Add-Content -Path $LogFile -Encoding UTF8
}
