<#
.SYNOPSIS
    Stops and removes a Windows Service.
.PARAMETER ServiceName
    SCM service name to remove.
#>
param(
    [Parameter(Mandatory)]
    [string]$ServiceName
)

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Warning "Service '$ServiceName' not found."
    exit 0
}

Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
sc.exe delete $ServiceName | Out-Null
Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
