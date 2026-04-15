// ── Log Analytics Workspace Module ────────────────────────────────────────
// Creates a Log Analytics workspace for centralized logging and diagnostics.
// Used for Application Insights, monitoring, and audit trail analysis.

param logAnalyticsName string
param location string
param retentionInDays int = 30
param sku string = 'PerGB2018'

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: sku
    }
    retentionInDays: retentionInDays
  }
}

output workspaceId string = logAnalyticsWorkspace.id
output workspaceName string = logAnalyticsWorkspace.name
output customerId string = logAnalyticsWorkspace.properties.customerId
