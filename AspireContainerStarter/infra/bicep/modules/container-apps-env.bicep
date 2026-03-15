// ─────────────────────────────────────────────────────────────────────────────
// Container Apps Environment — Workload-Profile mode
//
// Three profiles are defined:
//   Consumption    → API  (serverless, shared multi-tenant, pay-per-use)
//   standard-worker (D4) → Calc1Worker  (4 vCPU / 16 GB dedicated)
//   heavy-worker    (D8) → Calc2Worker  (8 vCPU / 32 GB dedicated)
//
// Workers that need even more compute can be moved to D16/D32; just add a
// new profile entry — no other resource needs to change.
// ─────────────────────────────────────────────────────────────────────────────

@description('Container Apps Environment name.')
param name string

@description('Azure region.')
param location string = resourceGroup().location

@description('Log Analytics workspace resource ID for OTel/diagnostic logs.')
param logAnalyticsWorkspaceId string = ''

@description('Maximum dedicated nodes in the standard-worker (D4) pool.')
param standardWorkerMaxNodes int = 10

@description('Maximum dedicated nodes in the heavy-worker (D8) pool.')
param heavyWorkerMaxNodes int = 10

// ── Environment ──────────────────────────────────────────────────────────────

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  properties: {
    appLogsConfiguration: logAnalyticsWorkspaceId != '' ? {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2022-10-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2022-10-01').primarySharedKey
      }
    } : null

    workloadProfiles: [
      // Serverless — no minimum node count, billed per vCPU·s used.
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
      // General-purpose dedicated pool for standard workers (Calc1Worker).
      // D4 = 4 vCPU / 16 GB RAM per node; each replica uses a slice.
      {
        name: 'standard-worker'
        workloadProfileType: 'D4'
        minimumCount: 0          // Scale pool to zero when no replicas scheduled.
        maximumCount: standardWorkerMaxNodes
      }
      // High-compute dedicated pool for heavier workers (Calc2Worker).
      // D8 = 8 vCPU / 32 GB RAM per node.
      {
        name: 'heavy-worker'
        workloadProfileType: 'D8'
        minimumCount: 0
        maximumCount: heavyWorkerMaxNodes
      }
    ]
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output id string = env.id
output defaultDomain string = env.properties.defaultDomain
output staticIp string = env.properties.staticIp
