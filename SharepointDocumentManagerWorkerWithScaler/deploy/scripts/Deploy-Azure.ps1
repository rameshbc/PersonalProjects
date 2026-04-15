#!/usr/bin/env pwsh
<#
.SYNOPSIS
Deploys SharePoint Document Manager infrastructure and services to Azure.

.DESCRIPTION
Complete one-command production deployment script that:
1. Authenticates to Azure
2. Creates resource group
3. Deploys Bicep infrastructure (App Services, SQL DB, Key Vault, Managed Identity)
4. Runs EF Core database migrations
5. Deploys Docker containers from ACR
6. Verifies health checks
7. Configures role-based access (managed identity permissions)

.PARAMETER SubscriptionId
Azure subscription ID to deploy to (required)

.PARAMETER Environment
Deployment environment: dev, staging, prod (default: staging)

.PARAMETER Location
Azure region for deployment (default: eastus)

.PARAMETER AcrName
Azure Container Registry name (default: from config)

.PARAMETER ImageTag
Docker image tag (default: latest)

.PARAMETER DryRun
Test deployment without making changes

.PARAMETER CredentialMode
Authentication mode: ManagedIdentity, ServicePrincipal, Interactive (default: Interactive)

.EXAMPLE
.\Deploy-Azure.ps1 -SubscriptionId "00000000-0000-0000-0000-000000000000" -Environment prod -Location eastus
#>

param(
    [Parameter(Mandatory = $true, HelpMessage = 'Azure Subscription ID')]
    [string]$SubscriptionId,

    [Parameter(HelpMessage = 'Environment: dev, staging, prod')]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment = 'staging',

    [Parameter(HelpMessage = 'Azure region')]
    [string]$Location = 'eastus',

    [Parameter(HelpMessage = 'Azure Container Registry name')]
    [string]$AcrName,

    [Parameter(HelpMessage = 'Docker image tag')]
    [string]$ImageTag = 'latest',

    [Parameter(HelpMessage = 'Test deployment without making changes')]
    [switch]$DryRun,

    [Parameter(HelpMessage = 'Authentication mode')]
    [ValidateSet('ManagedIdentity', 'ServicePrincipal', 'Interactive')]
    [string]$CredentialMode = 'Interactive'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ── Constants ──────────────────────────────────────────────────────────────
$projectName = 'spodm'
$resourceGroupName = "$projectName-$Environment-rg"
$deploymentName = "$projectName-$Environment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

# SKU configuration per environment
$skuConfig = @{
    dev     = @{ api = 'B1'; admin = 'B1'; worker = 'B1'; sql = 'Basic'; sqlCapacity = 5 }
    staging = @{ api = 'B2'; admin = 'B2'; worker = 'B2'; sql = 'Standard'; sqlCapacity = 50 }
    prod    = @{ api = 'B3'; admin = 'B2'; worker = 'B2'; sql = 'Standard'; sqlCapacity = 100 }
}

$sku = $skuConfig[$Environment]

# ── Helper Functions ───────────────────────────────────────────────────────
function Write-Header {
    param([string]$Text)
    Write-Host "`n$('=' * 80)" -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host "$('=' * 80)" -ForegroundColor Cyan
}

function Write-Step {
    param([int]$Step, [string]$Text)
    Write-Host "`n[STEP $Step] $Text" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Text)
    Write-Host "✓ $Text" -ForegroundColor Green
}

function Write-Error-Custom {
    param([string]$Text)
    Write-Host "✗ $Text" -ForegroundColor Red
}

function Test-AzureAuthentication {
    try {
        $context = Get-AzContext -ErrorAction Stop
        if ($context) {
            Write-Success "Authenticated as $($context.Account.Id)"
            return $true
        }
    }
    catch {
        return $false
    }
}

function Authenticate-Azure {
    Write-Step 1 "Azure Authentication"

    if (Test-AzureAuthentication) {
        $current = Get-AzContext
        Write-Host "Connected as: $($current.Account.Id) (Subscription: $($current.Subscription.Name))"
        return
    }

    switch ($CredentialMode) {
        'Interactive' {
            Write-Host "Opening browser for authentication..."
            Connect-AzAccount -Subscription $SubscriptionId | Out-Null
        }
        'ServicePrincipal' {
            $tenantId = Read-Host "Enter Tenant ID"
            $appId = Read-Host "Enter App ID"
            $secret = Read-Host "Enter Client Secret" -AsSecureString
            $credential = New-Object System.Management.Automation.PSCredential($appId, $secret)
            Connect-AzAccount -ServicePrincipal -Credential $credential -TenantId $tenantId `
                -Subscription $SubscriptionId | Out-Null
        }
        'ManagedIdentity' {
            Connect-AzAccount -Identity -Subscription $SubscriptionId | Out-Null
        }
    }

    Write-Success "Connected to Azure"
    Select-AzSubscription -SubscriptionId $SubscriptionId | Out-Null
    Write-Success "Selected subscription: $SubscriptionId"
}

function Confirm-Deployment {
    Write-Header "DEPLOYMENT SUMMARY"
    Write-Host "Environment:        $Environment"
    Write-Host "Location:           $Location"
    Write-Host "Resource Group:     $resourceGroupName"
    Write-Host "API SKU:            $($sku.api)"
    Write-Host "Admin SKU:          $($sku.admin)"
    Write-Host "Worker SKU:         $($sku.worker)"
    Write-Host "SQL SKU:            $($sku.sql) (DTU: $($sku.sqlCapacity))"
    Write-Host "Container Registry: $AcrName"
    Write-Host "Image Tag:          $ImageTag"
    Write-Host "Dry Run:            $DryRun"

    if (-not $DryRun) {
        $response = Read-Host "`nProceed with deployment? (yes/no)"
        if ($response -ne 'yes') {
            Write-Host "Deployment cancelled." -ForegroundColor Yellow
            exit 1
        }
    }
}

function Create-ResourceGroup {
    Write-Step 2 "Create Resource Group"

    $rg = Get-AzResourceGroup -Name $resourceGroupName -ErrorAction SilentlyContinue
    if ($rg) {
        Write-Success "Resource group already exists: $resourceGroupName"
        return
    }

    Write-Host "Creating resource group: $resourceGroupName in $Location"
    if ($DryRun) {
        Write-Host "  [DRY RUN] Would create resource group"
        return
    }

    New-AzResourceGroup -Name $resourceGroupName -Location $Location -Tags @{
        'Environment' = $Environment
        'Project'     = $projectName
        'DeployedBy'  = $env:USERNAME
        'DeployedAt'  = Get-Date -Format o
    } | Out-Null

    Write-Success "Resource group created: $resourceGroupName"
}

function Deploy-Infrastructure {
    Write-Step 3 "Deploy Bicep Infrastructure"

    $bicepPath = "$PSScriptRoot/../infrastructure/main.bicep"
    if (-not (Test-Path $bicepPath)) {
        throw "Bicep template not found: $bicepPath"
    }

    Write-Host "Template: $bicepPath"
    Write-Host "Deployment parameters:"
    Write-Host "  projectName: $projectName"
    Write-Host "  environment: $Environment"
    Write-Host "  apiSku: $($sku.api)"
    Write-Host "  acrName: $AcrName"

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would deploy Bicep template"
        return
    }

    try {
        $deployment = New-AzResourceGroupDeployment `
            -ResourceGroupName $resourceGroupName `
            -TemplateFile $bicepPath `
            -projectName $projectName `
            -environment $Environment `
            -location $Location `
            -apiSku $sku.api `
            -adminSku $sku.admin `
            -workerSku $sku.worker `
            -sqlSku $sku.sql `
            -sqlCapacity $sku.sqlCapacity `
            -acrName $AcrName `
            -imageTag $ImageTag `
            -azureTenantId (Read-Host "Enter Azure Tenant ID (for Graph API)" -AsSecureString) `
            -azureClientId (Read-Host "Enter Azure Client ID (for Graph API)" -AsSecureString) `
            -azureClientSecret (Read-Host "Enter Azure Client Secret (for Graph API)" -AsSecureString) `
            -sqlAdminUsername (Read-Host "Enter SQL Admin Username" -AsSecureString) `
            -sqlAdminPassword (Read-Host "Enter SQL Admin Password" -AsSecureString) `
            -Name $deploymentName `
            -TemplateParameterObject $templateParams

        Write-Success "Infrastructure deployed successfully"
        Write-Host "Deployment ID: $($deployment.DeploymentId)"

        # Output important resource info
        $deployment.Outputs | ForEach-Object {
            Write-Host "$($_.Key): $($_.Value.Value)" -ForegroundColor Gray
        }
    }
    catch {
        Write-Error-Custom "Deployment failed: $_"
        throw
    }
}

function Run-DatabaseMigrations {
    Write-Step 4 "Run EF Core Database Migrations"

    $migrationsPath = "$PSScriptRoot/../../src/SharepointDocManager.Infrastructure"
    if (-not (Test-Path $migrationsPath)) {
        Write-Host "  [SKIP] Infrastructure project not found" -ForegroundColor Gray
        return
    }

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would run database migrations"
        return
    }

    Write-Host "Installing dotnet-ef tool..."
    dotnet tool install --global dotnet-ef --version "* --pre" 2>&1 | Out-Null

    # Note: Connection string will be retrieved from Azure App Service configuration
    Write-Host "Applying EF Core migrations..."
    try {
        dotnet ef database update `
            --project $migrationsPath/SharepointDocManager.Infrastructure.csproj `
            --startup-project "$PSScriptRoot/../../src/SharepointDocManager.Api/SharepointDocManager.Api.csproj" `
            --configuration Release `
            --verbose 2>&1 | Out-Null

        Write-Success "Database migrations completed"
    }
    catch {
        Write-Error-Custom "Database migration failed: $_"
        throw
    }
}

function Update-AppServiceContainers {
    Write-Step 5 "Update App Service Docker Images"

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would update App Service containers"
        return
    }

    $acrUrl = "https://$AcrName.azurecr.io"
    $acrUsername = $AcrName
    $acrPassword = $(az acr credential show --name $AcrName --query passwords[0].value -o tsv)

    foreach ($service in @('api', 'worker', 'admin')) {
        $appName = "$projectName-$Environment-$service-app"

        Write-Host "Updating $service service ($appName)..."
        try {
            az webapp config container set `
                --resource-group $resourceGroupName `
                --name $appName `
                --docker-custom-image-name "$AcrName.azurecr.io/sharepoint-doc-manager-$service`:$ImageTag" `
                --docker-registry-server-url $acrUrl `
                --docker-registry-server-user $acrUsername `
                --docker-registry-server-password $acrPassword `
                2>&1 | Out-Null

            Write-Success "$service service updated"
        }
        catch {
            Write-Error-Custom "Failed to update $service : $_"
            throw
        }
    }
}

function Verify-HealthChecks {
    Write-Step 6 "Verify Deployment Health Checks"

    $apiUrl = "https://$projectName-$Environment-api-app.azurewebsites.net/health"
    $maxRetries = 30
    $retryDelaySeconds = 10

    Write-Host "Checking API health at: $apiUrl"

    for ($i = 1; $i -le $maxRetries; $i++) {
        try {
            $response = Invoke-WebRequest -Uri $apiUrl -TimeoutSec 5 -SkipHttpErrorCheck
            if ($response.StatusCode -eq 200) {
                Write-Success "API is healthy (attempt $i/$maxRetries)"
                return
            }
        }
        catch {
            # Silently continue
        }

        if ($i -lt $maxRetries) {
            Write-Host "Attempt $i/$maxRetries - Waiting for API to start..." -ForegroundColor Gray
            Start-Sleep -Seconds $retryDelaySeconds
        }
    }

    Write-Error-Custom "API health check failed after $maxRetries attempts"
    throw "Deployment verification failed"
}

function Configure-ManagedIdentityRoles {
    Write-Step 7 "Configure Managed Identity RBAC"

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would configure managed identity roles"
        return
    }

    $identity = Get-AzUserAssignedIdentity -ResourceGroupName $resourceGroupName -Name "$projectName-$Environment-identity" -ErrorAction SilentlyContinue
    if (-not $identity) {
        Write-Host "  [SKIP] Managed identity not found" -ForegroundColor Gray
        return
    }

    $sqlServer = Get-AzSqlServer -ResourceGroupName $resourceGroupName | Where-Object { $_.ServerName -like "$projectName*" } | Select-Object -First 1
    if (-not $sqlServer) {
        Write-Host "  [SKIP] SQL Server not found" -ForegroundColor Gray
        return
    }

    Write-Host "Assigning SQL Database Contributor role to managed identity..."
    try {
        New-AzRoleAssignment -ObjectId $identity.PrincipalId `
            -RoleDefinitionName 'SQL DB Contributor' `
            -Scope $sqlServer.ResourceId `
            -ErrorAction SilentlyContinue | Out-Null

        Write-Success "Managed identity roles configured"
    }
    catch {
        Write-Host "  [WARN] Role assignment may already exist: $_" -ForegroundColor Yellow
    }
}

function Show-PostDeploymentInstructions {
    Write-Header "DEPLOYMENT COMPLETE"

    Write-Host "`nDeployment Summary:"
    Write-Host "  Resource Group:  $resourceGroupName"
    Write-Host "  API URL:         https://$projectName-$Environment-api-app.azurewebsites.net"
    Write-Host "  Admin Portal:    https://$projectName-$Environment-admin-app.azurewebsites.net"

    Write-Host "`nNext Steps:"
    Write-Host "  1. Verify App Services are running:"
    Write-Host "     az webapp list -g $resourceGroupName --query '[].name'"
    Write-Host ""
    Write-Host "  2. View logs:"
    Write-Host "     az webapp log tail -g $resourceGroupName -n $projectName-$Environment-api-app"
    Write-Host ""
    Write-Host "  3. Configure Azure AD roles in Azure Portal:"
    Write-Host "     - Navigate to App Registrations > Your-App > API Permissions"
    Write-Host "     - Grant admin consent for Graph API permissions"
    Write-Host ""
    Write-Host "  4. Test endpoints:"
    Write-Host "     curl -s https://$projectName-$Environment-api-app.azurewebsites.net/health | jq ."
}

# ── Main Execution ─────────────────────────────────────────────────────────
try {
    Write-Header "SharePoint Document Manager - Azure Deployment Script"

    Confirm-Deployment
    Authenticate-Azure
    Create-ResourceGroup
    Deploy-Infrastructure
    Run-DatabaseMigrations
    Update-AppServiceContainers
    Verify-HealthChecks
    Configure-ManagedIdentityRoles
    Show-PostDeploymentInstructions

    Write-Success "`nDeployment completed successfully!"
    exit 0
}
catch {
    Write-Error-Custom "Deployment failed: $_"
    Write-Host "See logs above for details. Check resource group: $resourceGroupName" -ForegroundColor Yellow
    exit 1
}
