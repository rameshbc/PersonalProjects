// ── Main Bicep Template for SharePoint Document Manager ──────────────────────
// Orchestrates deployment of API, Worker, Admin services, SQL Database, Key Vault,
// and Managed Identity for production environment.

metadata description = 'SharePoint Document Manager production infrastructure'

@maxLength(11)
@description('Project name used for resource naming (e.g., "spodm" => spodm-rg, spodm-api-app, etc.)')
param projectName string

@description('Environment name: dev, staging, prod')
@allowed(['dev', 'staging', 'prod'])
param environment string

@description('Azure region')
param location string = resourceGroup().location

@description('API App Service tier: B1, B2, B3, S1, S2, P1V2, etc.')
param apiSku string = environment == 'prod' ? 'B3' : 'B1'

@description('Admin portal App Service tier')
param adminSku string = environment == 'prod' ? 'B2' : 'B1'

@description('Worker App Service tier')
param workerSku string = environment == 'prod' ? 'B2' : 'B1'

@description('SQL Database SKU: Basic, Standard, Premium')
param sqlSku string = environment == 'prod' ? 'Standard' : 'Basic'

@description('SQL Database capacity (DTU)')
param sqlCapacity int = environment == 'prod' ? 50 : 5

@description('Azure Container Registry SKU: Basic, Standard, Premium')
param acrSku string = 'Standard'

@description('Key Vault SKU: standard, premium')
param kvSku string = 'standard'

@description('Azure Tenant ID for Graph API app registration')
@secure()
param azureTenantId string

@description('Azure Client ID for Graph API (managed identity will use this for app-only access)')
@secure()
param azureClientId string

@description('Azure Client Secret for Graph API (stored in Key Vault)')
@secure()
param azureClientSecret string

@description('SQL Server admin username')
@secure()
param sqlAdminUsername string

@description('SQL Server admin password')
@secure()
param sqlAdminPassword string

@description('Docker image tag for API, Worker, Admin services')
param imageTag string = 'latest'

@description('Azure Container Registry name (where Docker images are stored)')
param acrName string

@description('Enable Application Insights monitoring')
param enableAppInsights bool = environment == 'prod'

// ── Naming Conventions ─────────────────────────────────────────────────────
var uniqueSuffix = uniqueString(resourceGroup().id)
var resourcePrefix = '${projectName}-${environment}'
var appServicePlanName = '${resourcePrefix}-asp'
var apiAppName = '${resourcePrefix}-api-app'
var workerAppName = '${resourcePrefix}-worker-app'
var adminAppName = '${resourcePrefix}-admin-app'
var sqlServerName = '${projectName}sql${uniqueSuffix}'
var sqlDatabaseName = '${projectName}db'
var kvName = '${projectName}kv${uniqueSuffix}'
var miName = '${resourcePrefix}-identity'
var appInsightsName = '${resourcePrefix}-ai'
var logAnalyticsName = '${resourcePrefix}-law'
var vnetName = '${resourcePrefix}-vnet'
var nsgName = '${resourcePrefix}-nsg'
var acrLoginServer = '${acrName}.azurecr.io'

// ── 1. User-Assigned Managed Identity ──────────────────────────────────────
module managedIdentity './modules/managedIdentity.bicep' = {
  name: 'managedIdentity'
  params: {
    miName: miName
    location: location
  }
}

// ── 2. Key Vault for Secrets ───────────────────────────────────────────────
module keyVault './modules/keyVault.bicep' = {
  name: 'keyVault'
  params: {
    kvName: kvName
    location: location
    kvSku: kvSku
    tenantId: subscription().tenantId
    principalId: managedIdentity.outputs.principalId
    azureTenantId: azureTenantId
    azureClientId: azureClientId
    azureClientSecret: azureClientSecret
    sqlAdminUsername: sqlAdminUsername
    sqlAdminPassword: sqlAdminPassword
  }
}

// ── 3. Log Analytics for Monitoring ────────────────────────────────────────
module logAnalytics './modules/logAnalytics.bicep' = if (enableAppInsights) {
  name: 'logAnalytics'
  params: {
    logAnalyticsName: logAnalyticsName
    location: location
  }
}

// ── 4. Application Insights ────────────────────────────────────────────────
module appInsights './modules/appInsights.bicep' = if (enableAppInsights) {
  name: 'appInsights'
  params: {
    appInsightsName: appInsightsName
    location: location
    logAnalyticsWorkspaceId: enableAppInsights ? logAnalytics.outputs.workspaceId : ''
  }
}

// ── 5. Networking (VNet, Subnets, NSG) ─────────────────────────────────────
module networking './modules/networking.bicep' = {
  name: 'networking'
  params: {
    vnetName: vnetName
    nsgName: nsgName
    location: location
  }
}

// ── 6. SQL Server and Database ─────────────────────────────────────────────
module sqlDatabase './modules/sqlDatabase.bicep' = {
  name: 'sqlDatabase'
  params: {
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    location: location
    sqlAdminUsername: sqlAdminUsername
    sqlAdminPassword: sqlAdminPassword
    sqlSku: sqlSku
    sqlCapacity: sqlCapacity
    managedIdentityPrincipalId: managedIdentity.outputs.principalId
  }
}

// ── 7. App Service Plan ────────────────────────────────────────────────────
module appServicePlan './modules/appServicePlan.bicep' = {
  name: 'appServicePlan'
  params: {
    appServicePlanName: appServicePlanName
    location: location
    kind: 'Linux'
    reserved: true
    sku: apiSku
  }
}

// ── 8. API App Service ─────────────────────────────────────────────────────
module apiApp './modules/appService.bicep' = {
  name: 'apiApp'
  params: {
    appName: apiAppName
    location: location
    appServicePlanId: appServicePlan.outputs.planId
    managedIdentityId: managedIdentity.outputs.id
    managedIdentityPrincipalId: managedIdentity.outputs.principalId
    dockerImage: '${acrLoginServer}/sharepoint-doc-manager-api:${imageTag}'
    acrLoginServer: acrLoginServer
    appSettings: {
      ASPNETCORE_ENVIRONMENT: environment
      ASPNETCORE_URLS: 'http://+:8080'
      DOTNET_RUNNING_IN_CONTAINER: 'true'
      ConnectionStrings__DefaultConnection: 'Server=tcp:${sqlDatabase.outputs.serverFqdn},1433;Initial Catalog=${sqlDatabaseName};Authentication=Active Directory Managed Identity;'
      Graph__AuthMode: 'ManagedIdentity'
      AZURE_TENANT_ID: azureTenantId
      AZURE_CLIENT_ID: azureClientId
      APPLICATIONINSIGHTS_CONNECTION_STRING: enableAppInsights ? appInsights.outputs.connectionString : ''
    }
    appServiceIdentity: {
      type: 'UserAssigned'
      userAssignedIdentities: {
        '${managedIdentity.outputs.id}': {}
      }
    }
  }
}

// ── 9. Admin Portal App Service ────────────────────────────────────────────
module adminApp './modules/appService.bicep' = {
  name: 'adminApp'
  params: {
    appName: adminAppName
    location: location
    appServicePlanId: appServicePlan.outputs.planId
    managedIdentityId: managedIdentity.outputs.id
    managedIdentityPrincipalId: managedIdentity.outputs.principalId
    dockerImage: '${acrLoginServer}/sharepoint-doc-manager-admin:${imageTag}'
    acrLoginServer: acrLoginServer
    appSettings: {
      ASPNETCORE_ENVIRONMENT: environment
      ASPNETCORE_URLS: 'http://+:8080'
      DOTNET_RUNNING_IN_CONTAINER: 'true'
      Api__BaseUrl: 'https://${apiApp.outputs.defaultHostname}'
      APPLICATIONINSIGHTS_CONNECTION_STRING: enableAppInsights ? appInsights.outputs.connectionString : ''
    }
    appServiceIdentity: {
      type: 'UserAssigned'
      userAssignedIdentities: {
        '${managedIdentity.outputs.id}': {}
      }
    }
  }
}

// ── 10. Worker App Service ─────────────────────────────────────────────────
module workerApp './modules/appService.bicep' = {
  name: 'workerApp'
  params: {
    appName: workerAppName
    location: location
    appServicePlanId: appServicePlan.outputs.planId
    managedIdentityId: managedIdentity.outputs.id
    managedIdentityPrincipalId: managedIdentity.outputs.principalId
    dockerImage: '${acrLoginServer}/sharepoint-doc-manager-worker:${imageTag}'
    acrLoginServer: acrLoginServer
    isWorker: true
    appSettings: {
      ASPNETCORE_ENVIRONMENT: environment
      DOTNET_RUNNING_IN_CONTAINER: 'true'
      ConnectionStrings__DefaultConnection: 'Server=tcp:${sqlDatabase.outputs.serverFqdn},1433;Initial Catalog=${sqlDatabaseName};Authentication=Active Directory Managed Identity;'
      Graph__AuthMode: 'ManagedIdentity'
      AZURE_TENANT_ID: azureTenantId
      AZURE_CLIENT_ID: azureClientId
      APPLICATIONINSIGHTS_CONNECTION_STRING: enableAppInsights ? appInsights.outputs.connectionString : ''
    }
    appServiceIdentity: {
      type: 'UserAssigned'
      userAssignedIdentities: {
        '${managedIdentity.outputs.id}': {}
      }
    }
  }
}

// ── Outputs ────────────────────────────────────────────────────────────────
output apiAppUrl string = 'https://${apiApp.outputs.defaultHostname}'
output adminAppUrl string = 'https://${adminApp.outputs.defaultHostname}'
output sqlServerFqdn string = sqlDatabase.outputs.serverFqdn
output sqlDatabaseName string = sqlDatabaseName
output keyVaultName string = keyVault.outputs.name
output managedIdentityId string = managedIdentity.outputs.id
output managedIdentityClientId string = managedIdentity.outputs.clientId
output appInsightsInstrumentationKey string = enableAppInsights ? appInsights.outputs.instrumentationKey : ''
