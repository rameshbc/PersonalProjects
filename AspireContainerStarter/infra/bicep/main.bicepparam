// main.bicepparam — parameter values for main.bicep
// Override imageTag at deploy time:
//   az deployment group create ... --parameters imageTag=<git-sha>

using './main.bicep'

// ── Required ──────────────────────────────────────────────────────────────────

param containerAppsEnvName = 'aspire-cae-prod'
param registryName         = 'aspireprodacr'         // short name, no .azurecr.io
param serviceBusNamespace  = 'aspire-prod-sb.servicebus.windows.net'
param imageTag             = 'latest'                // overridden by CD workflow

// ── Optional ──────────────────────────────────────────────────────────────────

param location                = 'australiaeast'
param logAnalyticsWorkspaceId = ''                   // set to workspace resource ID to enable logs
param standardWorkerMaxNodes  = 10                   // max D4 nodes for Calc1Worker pool
param heavyWorkerMaxNodes     = 10                   // max D8 nodes for Calc2Worker pool
