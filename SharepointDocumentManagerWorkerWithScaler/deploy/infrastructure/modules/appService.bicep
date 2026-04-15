// ── App Service Module ────────────────────────────────────────────────────
// Creates an App Service for hosting API, Admin portal, or Worker service.
// Configures:
// - Docker container deployment from ACR
// - Managed identity for authentication
// - Application settings for database, Graph API, App Insights
// - Health checks for API and Admin (skipped for Worker)
// - Auto-start on host restart

param appName string
param location string
param appServicePlanId string
param managedIdentityId string
param managedIdentityPrincipalId string
param dockerImage string
param acrLoginServer string
param isWorker bool = false

@description('App settings as key-value pairs (e.g., CONNECTION_STRING, ASPNETCORE_ENVIRONMENT)')
param appSettings object = {}

@description('App Service identity configuration')
param appServiceIdentity object = {
  type: 'UserAssigned'
  userAssignedIdentities: {}
}

resource appService 'Microsoft.Web/sites@2022-09-01' = {
  name: appName
  location: location
  identity: appServiceIdentity
  kind: 'app,linux,container'
  properties: {
    serverFarmId: appServicePlanId
    siteConfig: {
      alwaysOn: true
      appCommandLine: ''
      http20Enabled: true
      minTlsVersion: '1.2'
      linuxFxVersion: 'DOCKER|${dockerImage}'
      acrUseManagedIdentityCreds: true
      acrUserManagedIdentityId: managedIdentityId
      appSettings: [
        for key in items(appSettings): {
          name: key.key
          value: key.value
        }
      ]
      connectionStrings: []
      ftpsState: 'Disabled'
      numberOfWorkers: 1
      defaultDocuments: isWorker ? [] : ['index.html']
    }
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    virtualNetworkSubnetId: null
  }
}

// Configure container registry access via managed identity
resource appServiceConfig 'Microsoft.Web/sites/config@2022-09-01' = {
  parent: appService
  name: 'web'
  properties: {
    acrUseManagedIdentityCreds: true
    acrUserManagedIdentityId: managedIdentityId
    linuxFxVersion: 'DOCKER|${dockerImage}'
  }
}

// Startup task: ensure managed identity can pull from ACR
// This is handled by acrUseManagedIdentityCreds + proper role assignment in main.bicep

// Health check for web apps (API and Admin)
resource appServiceHealthCheck 'Microsoft.Web/sites/config@2022-09-01' = if (!isWorker) {
  parent: appService
  name: 'healthCheckPath'
  properties: {
    healthCheckPath: '/health'
  }
}

// Autoscale settings for production (optional)
// Scales between 1-3 instances based on CPU/Memory
resource autoScaleSettings 'Microsoft.Insights/autoscalesettings@2022-10-01' = {
  name: '${appName}-autoscale'
  location: location
  properties: {
    enabled: !isWorker && environment().name == 'AzureCloud' // Only for web apps in prod
    targetResourceUri: appService.id
    profiles: [
      {
        name: 'Auto scale based on CPU'
        capacity: {
          minimum: '1'
          maximum: '3'
          default: '1'
        }
        rules: [
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appService.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              operator: 'GreaterThan'
              threshold: 75
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appService.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              operator: 'LessThan'
              threshold: 25
            }
            scaleAction: {
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
        ]
      }
    ]
  }
}

output id string = appService.id
output name string = appService.name
output defaultHostname string = appService.properties.defaultHostName
output outboundIpAddresses array = split(appService.properties.outboundIpAddresses, ',')
