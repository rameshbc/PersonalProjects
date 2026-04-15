// ── App Service Plan Module ────────────────────────────────────────────────
// Creates a Linux App Service Plan for hosting API, Worker, and Admin services.
// Plan is shared across all three services to reduce costs while maintaining
// consistent performance characteristics.

param appServicePlanName string
param location string
param kind string = 'Linux'
param reserved bool = true
param sku string

resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: appServicePlanName
  location: location
  kind: kind
  sku: {
    name: sku
    tier: sku == 'B1' || sku == 'B2' || sku == 'B3' ? 'Basic' : sku == 'S1' || sku == 'S2' ? 'Standard' : 'Premium'
    capacity: 1 // Start with 1 instance, scale based on load
  }
  properties: {
    reserved: reserved
  }
}

output planId string = appServicePlan.id
output planName string = appServicePlan.name
