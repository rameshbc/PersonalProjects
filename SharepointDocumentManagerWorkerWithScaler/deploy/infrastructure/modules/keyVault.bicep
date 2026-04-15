// ── Key Vault Module ──────────────────────────────────────────────────────
// Creates an Azure Key Vault and populates it with secrets for:
// - Graph API credentials (tenant ID, client ID, client secret)
// - SQL Server admin credentials
// Settings are locked down so only the managed identity and admin can access.

param kvName string
param location string
param kvSku string
param tenantId string
param principalId string

@secure()
param azureTenantId string

@secure()
param azureClientId string

@secure()
param azureClientSecret string

@secure()
param sqlAdminUsername string

@secure()
param sqlAdminPassword string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    enabledForDeployment: true
    enabledForTemplateDeployment: true
    enabledForDiskEncryption: false
    tenantId: tenantId
    sku: {
      family: 'A'
      name: kvSku
    }
    accessPolicies: [
      {
        tenantId: tenantId
        objectId: principalId
        permissions: {
          keys: ['get', 'list']
          secrets: ['get', 'list']
          certificates: ['get', 'list']
        }
      }
    ]
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// Store Graph API credentials
resource graphTenantIdSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'GraphTenantId'
  properties: {
    value: azureTenantId
  }
}

resource graphClientIdSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'GraphClientId'
  properties: {
    value: azureClientId
  }
}

resource graphClientSecretSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'GraphClientSecret'
  properties: {
    value: azureClientSecret
  }
}

// Store SQL credentials
resource sqlUsernameSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SqlAdminUsername'
  properties: {
    value: sqlAdminUsername
  }
}

resource sqlPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SqlAdminPassword'
  properties: {
    value: sqlAdminPassword
  }
}

output name string = keyVault.name
output uri string = keyVault.properties.vaultUri
output id string = keyVault.id
