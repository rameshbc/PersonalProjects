<#
.SYNOPSIS
    Authentication helpers for Microsoft Graph API access.

.DESCRIPTION
    Provides token acquisition for three auth modes:
      - ManagedIdentity   : Azure IMDS endpoint — use this in production on Azure-hosted workloads.
      - ClientCredentials : Service principal with secret or certificate — use for dev/bootstrap scenarios
                            where MI is unavailable. Requires TenantId, ClientId, ClientSecret.
      - Interactive       : Browser-based login via the Az PowerShell module — use for ad-hoc operator runs.

    All modes return a raw Bearer token string that the Graph-Helpers wrapper accepts.

.NOTES
    Prerequisites:
      - ManagedIdentity   : Script runs on Azure VM / App Service / AKS with MI assigned.
      - ClientCredentials : Az.Accounts module NOT required. Only built-in Invoke-RestMethod.
      - Interactive       : Requires Az.Accounts module (Install-Module Az.Accounts).
#>

# ──────────────────────────────────────────────────────────────────────────────
# Public: Get-GraphAccessToken
# ──────────────────────────────────────────────────────────────────────────────
function Get-GraphAccessToken {
    <#
    .SYNOPSIS
        Acquires a Microsoft Graph access token using the specified auth mode.

    .PARAMETER AuthMode
        ManagedIdentity | ClientCredentials | Interactive

    .PARAMETER TenantId
        Entra ID tenant GUID. Required for ClientCredentials and Interactive modes.

    .PARAMETER ClientId
        App registration client ID. Required for ClientCredentials mode.
        For ManagedIdentity, leave empty — IMDS returns the MI token automatically.

    .PARAMETER ClientSecret
        App registration client secret. Required for ClientCredentials mode.
        Do NOT store secrets in script files — pass via environment variable or
        Azure Key Vault reference at runtime.

    .EXAMPLE
        # Managed Identity (production)
        $token = Get-GraphAccessToken -AuthMode ManagedIdentity

    .EXAMPLE
        # Client credentials (dev / bootstrap)
        $token = Get-GraphAccessToken `
            -AuthMode ClientCredentials `
            -TenantId  $env:TENANT_ID `
            -ClientId  $env:SP_CLIENT_ID `
            -ClientSecret $env:SP_CLIENT_SECRET

    .EXAMPLE
        # Interactive (operator ad-hoc run)
        $token = Get-GraphAccessToken -AuthMode Interactive -TenantId $env:TENANT_ID
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param (
        [Parameter(Mandatory)]
        [ValidateSet('ManagedIdentity', 'ClientCredentials', 'Interactive')]
        [string] $AuthMode,

        [string] $TenantId,
        [string] $ClientId,
        [string] $ClientSecret
    )

    $graphResource = 'https://graph.microsoft.com'

    switch ($AuthMode) {

        'ManagedIdentity' {
            # Azure Instance Metadata Service (IMDS) — no credentials needed.
            # The MI must be assigned to the compute resource before running.
            Write-Verbose '[Auth] Acquiring token via Managed Identity (IMDS)...'

            $imdsUrl = 'http://169.254.169.254/metadata/identity/oauth2/token' `
                       + "?api-version=2018-02-01&resource=$graphResource/"

            try {
                $response = Invoke-RestMethod `
                    -Uri     $imdsUrl `
                    -Headers @{ Metadata = 'true' } `
                    -Method  GET `
                    -ErrorAction Stop

                Write-Verbose '[Auth] Managed Identity token acquired successfully.'
                return $response.access_token
            }
            catch {
                throw "[Auth] Failed to acquire Managed Identity token. " +
                      "Ensure this machine has a Managed Identity assigned. Error: $_"
            }
        }

        'ClientCredentials' {
            # OAuth 2.0 client_credentials flow — app-only, no user context.
            # SECURITY: Never hardcode ClientSecret. Use environment variables or Key Vault.
            if (-not $TenantId -or -not $ClientId -or -not $ClientSecret) {
                throw '[Auth] ClientCredentials mode requires TenantId, ClientId, and ClientSecret.'
            }

            Write-Verbose "[Auth] Acquiring token via Client Credentials for app '$ClientId'..."

            $tokenUrl = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
            $body = @{
                grant_type    = 'client_credentials'
                client_id     = $ClientId
                client_secret = $ClientSecret
                scope         = "$graphResource/.default"
            }

            try {
                $response = Invoke-RestMethod `
                    -Uri         $tokenUrl `
                    -Method      POST `
                    -Body        $body `
                    -ContentType 'application/x-www-form-urlencoded' `
                    -ErrorAction Stop

                Write-Verbose '[Auth] Client Credentials token acquired successfully.'
                return $response.access_token
            }
            catch {
                throw "[Auth] Failed to acquire Client Credentials token. " +
                      "Check TenantId, ClientId, and ClientSecret. Error: $_"
            }
        }

        'Interactive' {
            # Delegated flow via Az PowerShell module — prompts browser login.
            # The signed-in user must be a SharePoint Admin or Global Admin to
            # grant site-level permissions to the MI app.
            if (-not $TenantId) {
                throw '[Auth] Interactive mode requires TenantId.'
            }

            Write-Verbose '[Auth] Acquiring token interactively via Az module...'

            if (-not (Get-Module -ListAvailable -Name Az.Accounts)) {
                throw '[Auth] Az.Accounts module not installed. Run: Install-Module Az.Accounts'
            }

            Connect-AzAccount -TenantId $TenantId -ErrorAction Stop | Out-Null
            $tokenObj = Get-AzAccessToken -ResourceUrl $graphResource -ErrorAction Stop

            Write-Verbose '[Auth] Interactive token acquired successfully.'
            return $tokenObj.Token
        }
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# Public: Test-TokenExpiry  (lightweight — checks exp claim in JWT)
# ──────────────────────────────────────────────────────────────────────────────
function Test-TokenExpiringSoon {
    <#
    .SYNOPSIS
        Returns $true if the JWT token expires within the next 5 minutes.
        Use this to decide whether to re-acquire before a long operation.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param (
        [Parameter(Mandatory)]
        [string] $Token
    )

    try {
        # JWT is Base64Url encoded: header.payload.signature
        $payloadBase64 = $Token.Split('.')[1]
        # Fix Base64Url padding
        $padded = $payloadBase64.Replace('-', '+').Replace('_', '/')
        switch ($padded.Length % 4) {
            2 { $padded += '==' }
            3 { $padded += '='  }
        }
        $claims = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($padded)) |
                  ConvertFrom-Json

        $expiresAt  = [DateTimeOffset]::FromUnixTimeSeconds($claims.exp)
        $expiresIn  = ($expiresAt - [DateTimeOffset]::UtcNow).TotalMinutes

        return $expiresIn -lt 5
    }
    catch {
        # If we can't decode, assume it might be expiring
        Write-Warning '[Auth] Could not decode token expiry — assuming near-expiry.'
        return $true
    }
}
