<#
.SYNOPSIS
    Installs a worker as a Windows Service via SCM.
.PARAMETER ServiceName
    SCM service name (must be unique per service instance).
.PARAMETER ExePath
    Full path to the worker executable.
.PARAMETER DisplayName
    Human-readable display name shown in Services console.
.PARAMETER Description
    Service description.
.EXAMPLE
    .\install-service.ps1 -ServiceName "WorkerSvc-OrdersQueue" `
                          -ExePath "C:\Services\SampleWorker.Queue\SampleWorker.Queue.exe" `
                          -DisplayName "Orders Queue Worker"
#>
param(
    [Parameter(Mandatory)]
    [string]$ServiceName,

    [Parameter(Mandatory)]
    [string]$ExePath,

    [string]$DisplayName    = $ServiceName,
    [string]$Description    = "Azure Service Bus messaging worker",
    [string]$StartupType    = "Automatic",
    [string]$RunAsUser      = "LocalSystem"
)

if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found: $ExePath"
    exit 1
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Warning "Service '$ServiceName' already exists. Stopping and removing before reinstall."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName `
            -BinaryPathName $ExePath `
            -DisplayName $DisplayName `
            -Description $Description `
            -StartupType $StartupType

if ($RunAsUser -ne "LocalSystem") {
    $cred = Get-Credential -UserName $RunAsUser -Message "Enter password for service account"
    $svc  = Get-WmiObject Win32_Service -Filter "Name='$ServiceName'"
    $svc.Change($null,$null,$null,$null,$null,$null,$RunAsUser,$cred.GetNetworkCredential().Password) | Out-Null
}

Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' installed and started." -ForegroundColor Green
