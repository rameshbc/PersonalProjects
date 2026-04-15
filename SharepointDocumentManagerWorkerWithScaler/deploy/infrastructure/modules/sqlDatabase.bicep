// ── SQL Database Module ────────────────────────────────────────────────────
// Creates Azure SQL Server and Database with managed identity authentication.
// EF Core connects using DefaultAzureCredential (no secrets in connection string).
// Database auto-initialized via EF Core migrations on app startup.

param sqlServerName string
param sqlDatabaseName string
param location string

@secure()
param sqlAdminUsername string

@secure()
param sqlAdminPassword string

param sqlSku string
param sqlCapacity int
param managedIdentityPrincipalId string

resource sqlServer 'Microsoft.Sql/servers@2019-06-01' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminUsername
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Application'
      login: 'SharepointDocManager'
      sid: managedIdentityPrincipalId
      tenantId: subscription().tenantId
    }
  }
}

// Allow Azure services to access SQL Server
resource sqlServerFirewall 'Microsoft.Sql/servers/firewallRules@2014-04-01' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// SQL Database
resource sqlDatabase 'Microsoft.Sql/servers/databases@2019-06-01' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: sqlSku
    capacity: sqlCapacity
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: sqlSku == 'Basic' ? 2147483648 : 268435456000 // 2GB for Basic, 250GB for Standard
    readScaleOutEnabled: sqlSku != 'Basic'
    zoneRedundant: false
    isLedgerOn: false
  }
}

// Elastic pool optional add-on (for scaling multiple databases)
// Commented out unless needed for multi-tenant scaling

output serverId string = sqlServer.id
output serverName string = sqlServer.name
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseId string = sqlDatabase.id
output databaseName string = sqlDatabase.name
