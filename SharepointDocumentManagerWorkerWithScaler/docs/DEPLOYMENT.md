# Production Deployment Guide

Complete guide for deploying SharePoint Document Manager to Azure production environment.

## Prerequisites

- **Azure Subscription** with owner/contributor role
- **Azure Container Registry** (ACR) created and accessible
- **Entra ID Application Registration** with Graph API app-only permissions granted
- **GitHub Repository Secrets** configured (if using CI/CD)
- **Azure CLI** installed locally
- **PowerShell 7+** installed

## Step 1: Prepare Entra ID Application Registration

### 1.1 Create an Entra ID App Registration

```powershell
# Enable managed identity for app-only access
az ad app create `
  --display-name "SharePoint Document Manager" `
  --description "Multi-tenant document management for SharePoint Online and Embedded"
```

### 1.2 Grant Graph API Permissions (App-Only)

In Azure Portal:
1. Navigate to **App Registrations** > Your App > **API Permissions**
2. Click **Add a permission** > **Microsoft Graph** > **Application permissions**
3. Search for and grant:
   - `Sites.Selected` — Site/container-scoped permissions
   - `Files.ReadWrite.All` — Read/write files and folders
   - `User.Read.All` — Read user profiles (for permission groups)
4. Click **Grant admin consent** (requires Global Administrator or Cloud Application Administrator role)

### 1.3 Create Client Secret

1. Navigate to **Certificates & secrets** > **Client secrets** > **New client secret**
2. Set expiry to 2 years
3. Copy the value securely (you'll need this for deployment)

### 1.4 Note the IDs

- **Tenant ID** (from Overview)
- **Application ID** (Client ID) from Overview
- **Client Secret** (from Certificates & secrets)

## Step 2: Configure GitHub Actions Secrets

For CI/CD automation, add these secrets to your GitHub repository:

```
Settings > Secrets and variables > Actions > New repository secret
```

| Secret Name | Value |
|------------|-------|
| `AZURE_SUBSCRIPTION_ID` | Your Azure subscription ID |
| `AZURE_TENANT_ID` | Entra ID tenant ID |
| `AZURE_CLIENT_ID` | App registration client ID |
| `AZURE_CLIENT_SECRET` | App registration client secret |
| `AZURE_CONTAINER_REGISTRY_NAME` | Your ACR name (without .azurecr.io) |
| `SQL_ADMIN_USERNAME` | SQL Server admin username (e.g., `sqladmin`) |
| `SQL_ADMIN_PASSWORD` | SQL Server admin password (complex) |
| `SLACK_WEBHOOK_URL` | (Optional) Slack webhook for deployment notifications |

## Step 3: Build and Push Docker Images

### 3.1 Local Build (for testing)

```bash
# Build all Docker images
docker-compose build

# Or build individually
docker build -f deploy/docker/Dockerfile.api -t spodm-api:latest .
docker build -f deploy/docker/Dockerfile.worker -t spodm-worker:latest .
docker build -f deploy/docker/Dockerfile.admin -t spodm-admin:latest .
```

### 3.2 Push to Azure Container Registry

```powershell
# Login to ACR
az acr login --name your-acr-name

# Tag images
$acrName = "your-acr-name"
$imageTag = "v1.0.0"

docker tag spodm-api:latest "$acrName.azurecr.io/sharepoint-doc-manager-api:$imageTag"
docker tag spodm-worker:latest "$acrName.azurecr.io/sharepoint-doc-manager-worker:$imageTag"
docker tag spodm-admin:latest "$acrName.azurecr.io/sharepoint-doc-manager-admin:$imageTag"

# Push to ACR
docker push "$acrName.azurecr.io/sharepoint-doc-manager-api:$imageTag"
docker push "$acrName.azurecr.io/sharepoint-doc-manager-worker:$imageTag"
docker push "$acrName.azurecr.io/sharepoint-doc-manager-admin:$imageTag"

# Verify
az acr repository list --name $acrName
```

## Step 4: Deploy via PowerShell Script (Recommended)

### 4.1 Run the Deployment Script

```powershell
cd deploy/scripts

# Review deployment configuration
.\Deploy-Azure.ps1 `
  -SubscriptionId "your-subscription-id" `
  -Environment "staging" `
  -Location "eastus" `
  -AcrName "your-acr-name" `
  -ImageTag "v1.0.0"
```

The script will prompt for:
- Azure authentication
- Confirmation of deployment parameters
- Graph API credentials (Tenant ID, Client ID, Secret)
- SQL Server admin credentials

### 4.2 What the Script Does

1. ✅ Authenticates to Azure
2. ✅ Creates Resource Group
3. ✅ Deploys Bicep infrastructure (App Services, SQL DB, Key Vault, MI)
4. ✅ Runs EF Core database migrations
5. ✅ Updates App Service containers
6. ✅ Verifies health checks
7. ✅ Configures Managed Identity RBAC

### 4.3 Post-Deployment Verification

```powershell
# Check App Services are running
az webapp list -g spodm-{environment}-rg --query '[].name'

# View logs
az webapp log tail -g spodm-{environment}-rg -n spodm-{environment}-api-app

# Test health endpoint
curl https://spodm-{environment}-api-app.azurewebsites.net/health
```

## Step 5: Deploy via GitHub Actions (CI/CD)

### 5.1 Push Changes to Trigger Workflow

```bash
git add .
git commit -m "Deploy to production"
git push origin main
```

This automatically triggers `.github/workflows/deploy.yml` which:
- Builds and tests the solution
- Builds Docker images and pushes to ACR
- Deploys infrastructure via Bicep
- Runs migrations
- Verifies health checks
- Posts notification to Slack

### 5.2 Monitor CI/CD Progress

1. Go to your GitHub repository
2. Click **Actions** tab
3. Select the running workflow
4. View logs for each job (Build, Build Docker, Deploy)

### 5.3 Troubleshoot CI/CD Failures

```bash
# Check workflow logs for errors
# Look for specific job failures in GitHub Actions UI

# Common issues:
# - Docker login failed: Verify ACR credentials in secrets
# - Bicep deployment failed: Check parameter syntax
# - Migration failed: Verify SQL connection string
# - Health check timeout: Check API logs in Azure Portal
```

## Step 6: Manual Deployment via Azure CLI

If you prefer to deploy without the PowerShell script:

```powershell
# 1. Create Resource Group
$rg = "spodm-prod-rg"
az group create --name $rg --location eastus

# 2. Deploy Bicep Template
az deployment group create `
  --resource-group $rg `
  --template-file deploy/infrastructure/main.bicep `
  --parameters `
    projectName=spodm `
    environment=prod `
    acrName=your-acr-name `
    azureTenantId=your-tenant-id `
    azureClientId=your-client-id `
    azureClientSecret=your-client-secret `
    sqlAdminUsername=sqladmin `
    sqlAdminPassword=YourSecurePassword123!

# 3. Run Database Migrations
dotnet ef database update `
  --project src/SharepointDocManager.Infrastructure `
  --startup-project src/SharepointDocManager.Api

# 4. Update App Services with Docker images
foreach ($service in @('api', 'worker', 'admin')) {
  az webapp config container set `
    --resource-group $rg `
    --name spodm-prod-$service-app `
    --docker-custom-image-name your-acr-name.azurecr.io/sharepoint-doc-manager-$service:v1.0.0 `
    --docker-registry-server-url https://your-acr-name.azurecr.io `
    --docker-registry-server-user your-client-id `
    --docker-registry-server-password your-client-secret
}
```

## Step 7: Post-Deployment Configuration

### 7.1 Configure Application Insights

1. Go to Azure Portal > Your Resource Group > Application Insights resource
2. Click **Application settings**
3. Verify Application ID is populated
4. Configure alert rules for errors/failures

### 7.2 Enable Application Logging

```bash
az webapp log config `
  --resource-group spodm-prod-rg `
  --name spodm-prod-api-app `
  --web-server-logging filesystem `
  --detailed-error-logging true `
  --failed-request-tracing true
```

### 7.3 Configure Auto-Scaling

```bash
# For production, enable auto-scaling
az monitor autoscale create `
  --resource-group spodm-prod-rg `
  --resource spodm-prod-api-app `
  --resource-type "Microsoft.Web/sites" `
  --min-count 2 `
  --max-count 5 `
  --count 2
```

### 7.4 Configure Custom Domain & SSL

```bash
# Bind custom domain and enable HTTPS
az webapp config ssl bind `
  --resource-group spodm-prod-rg `
  --name spodm-prod-api-app `
  --certificate-thumbprint YOUR_CERT_THUMBPRINT
```

## Step 8: Grant Managed Identity to Existing SharePoint Sites

Use **Script A** to grant the Managed Identity permissions to existing SharePoint Online sites:

```powershell
# Prepare CSV with existing site URLs
# ClientId,SiteUrl,SiteId
# client-001,https://partner1.sharepoint.com/sites/docs,site-id-1
# client-002,https://partner2.sharepoint.com/sites/shared,site-id-2

.\deploy\scripts\migration\ScriptA-GrantMIPermissions\Grant-ManagedIdentityPermissions.ps1 `
  -ConfigPath ./clients.csv `
  -TenantId your-tenant-id `
  -ManagedIdentityObjectId (Get-AzUserAssignedIdentity -Name spodm-prod-identity).PrincipalId `
  -DryRun:$false -Verbose
```

## Step 9: Migrate Content from SP to SPE (Optional)

Use **Script B** to migrate documents from SharePoint Online to SharePoint Embedded:

```powershell
# Prepare migration config
$config = @{
  tenantId = "your-tenant-id"
  auth = "ManagedIdentity"
  clients = @(
    @{
      id = "client-001"
      sourceType = "SharePointOnline"
      sourceUrl = "https://partner1.sharepoint.com/sites/docs"
      sourceDocLibrary = "DocLibrary-A"
      targetType = "SharePointEmbedded"
      targetContainerId = "container-id-1"
    }
  )
}

.\deploy\scripts\migration\ScriptB-MigrateSpToSpe\Migrate-SpToSpe.ps1 `
  -ConfigPath ./migration-config.json `
  -DryRun:$false -Verbose
```

## Monitoring & Operations

### Health Checks

```bash
# API health check
curl https://spodm-prod-api-app.azurewebsites.net/health

# View real-time logs
az webapp log tail -g spodm-prod-rg -n spodm-prod-api-app

# Download full logs
az webapp log download -g spodm-prod-rg -n spodm-prod-api-app --log-file logs.zip
```

### Key Vault Secrets

```bash
# View secrets in Key Vault
az keyvault secret list --vault-name spodmkv{unique-suffix}

# Get specific secret
az keyvault secret show --vault-name spodmkv{unique-suffix} --name GraphClientSecret --query value
```

### Database Backups

```bash
# Automatic backups are enabled (7-day retention for Standard tier)
# Manual backup
az sql db backup create `
  --resource-group spodm-prod-rg `
  --server spodmsql{unique-suffix} `
  --database spodmdb `
  --name manual-backup-$(Get-Date -Format yyyyMMdd)
```

## Rollback Procedures

### Rollback to Previous Deployment

```bash
# List deployment history
az deployment group list -g spodm-prod-rg --query '[].name'

# Redeploy from previous template
az deployment group create `
  --resource-group spodm-prod-rg `
  --template-file deploy/infrastructure/main.bicep `
  --parameters previous-params.json
```

### Rollback Docker Images

```bash
# Update App Service to use previous image tag
az webapp config container set `
  --resource-group spodm-prod-rg `
  --name spodm-prod-api-app `
  --docker-custom-image-name your-acr-name.azurecr.io/sharepoint-doc-manager-api:v0.9.0
```

## Troubleshooting Deployment Issues

### Issue: "Resource providers not registered"

```powershell
# Register required resource providers
az provider register --namespace Microsoft.Web
az provider register --namespace Microsoft.Sql
az provider register --namespace Microsoft.KeyVault
az provider register --namespace Microsoft.Insights
az provider register --namespace Microsoft.Authorization
```

### Issue: "Deployment script timed out"

```bash
# Increase timeout and retry
az deployment group create `
  --resource-group spodm-prod-rg `
  --template-file deploy/infrastructure/main.bicep `
  --parameters ... `
  --no-wait  # Run in background

# Check deployment status
az deployment group show -g spodm-prod-rg -n spodm-prod-yyyyMMdd-HHmmss
```

### Issue: "Graph API permissions not granted"

```powershell
# Verify app registration has admin consent
az ad app permission list --id your-app-id

# Grant admin consent (if not already granted)
az rest --method POST `
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/{service-principal-id}/appRoleAssignments" `
  --body '{"principalId":"...","resourceId":"...","appRoleId":"..."}'
```

## Performance Tuning

| Setting | Recommendation | Impact |
|---------|----------------|--------|
| App Service SKU | B3 (prod), B2 (staging) | Higher CPU/Memory = faster response |
| SQL Database DTU | 100+ (prod) | Higher DTU = more concurrent queries |
| Document batch size | 20 | Graph API /batch limit |
| Upload channel capacity | 100 | Higher = more memory, faster throughput |
| Polly retry attempts | Gold: 10 | More retries = better resilience, longer latency |

## Cleanup

To delete resources and stop incurring costs:

```bash
# Delete entire resource group (irreversible!)
az group delete --name spodm-prod-rg --yes --no-wait

# Delete ACR images
az acr repository delete --name your-acr-name --repository sharepoint-doc-manager-api

# Delete Key Vault
az keyvault delete --name spodmkv{unique-suffix} --yes
```

---

**Last Updated**: April 7, 2026
**Version**: 1.0.0
