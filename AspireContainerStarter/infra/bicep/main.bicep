// ─────────────────────────────────────────────────────────────────────────────
// main.bicep — Container Apps deployment orchestrator
//
// Provisions:
//   • Container Apps Environment with three workload profiles
//   • API Container App          (Consumption profile, HTTP scaling)
//   • Calc1Worker Container App  (D4 standard-worker, SB + CPU scaling)
//   • Calc2Worker Container App  (D8 heavy-worker,    SB + CPU scaling)
//
// Existing resources (ACR, Service Bus, SQL, Redis, Key Vault) are consumed
// by reference — this module does not provision them.
//
// Deployment (from repo root):
//   az deployment group create \
//     --resource-group <rg> \
//     --template-file infra/bicep/main.bicep \
//     --parameters infra/bicep/main.bicepparam \
//     --parameters imageTag=<github-sha>
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Container Apps Environment name.')
param containerAppsEnvName string

@description('ACR short name — the part before .azurecr.io.')
param registryName string

@description('Image tag to deploy across all services.')
param imageTag string

@description('Service Bus fully-qualified namespace (e.g. my-ns.servicebus.windows.net).')
param serviceBusNamespace string

@description('Log Analytics workspace resource ID (optional — leave empty to skip).')
param logAnalyticsWorkspaceId string = ''

@description('Max dedicated D4 nodes in the standard-worker pool.')
param standardWorkerMaxNodes int = 10

@description('Max dedicated D8 nodes in the heavy-worker pool.')
param heavyWorkerMaxNodes int = 10

// ── Derived values ────────────────────────────────────────────────────────────

var registryServer = '${registryName}.azurecr.io'

// ── Container Apps Environment ────────────────────────────────────────────────

module cae 'modules/container-apps-env.bicep' = {
  name: 'container-apps-env'
  params: {
    name: containerAppsEnvName
    location: location
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
    standardWorkerMaxNodes: standardWorkerMaxNodes
    heavyWorkerMaxNodes: heavyWorkerMaxNodes
  }
}

// ── API ───────────────────────────────────────────────────────────────────────

module api 'modules/api-container-app.bicep' = {
  name: 'api-container-app'
  params: {
    location: location
    containerAppsEnvId: cae.outputs.id
    registryServer: registryServer
    imageTag: imageTag
    // Defaults: minReplicas=1, maxReplicas=5, concurrentRequests=50
  }
}

// ── Calc1 Worker — standard compute (D4) ─────────────────────────────────────

module calc1Worker 'modules/worker-container-app.bicep' = {
  name: 'calc1-worker-container-app'
  params: {
    name: 'aspire-calc1-worker'
    location: location
    containerAppsEnvId: cae.outputs.id
    workloadProfileName: 'standard-worker'   // D4: 4 vCPU / 16 GB per node
    registryServer: registryServer
    imageName: 'calc1-worker'
    imageTag: imageTag
    serviceBusNamespace: serviceBusNamespace
    queueName: 'calc1-jobs'
    cpu: '2'         // 2 vCPU per replica (within D4 node limits)
    memory: '4Gi'
    minReplicas: 0   // Scale to zero when queue is empty
    maxReplicas: 50
    messagesPerReplica: 5
    cpuScaleThreshold: 70
  }
}

// ── Calc2 Worker — heavy compute (D8) ────────────────────────────────────────

module calc2Worker 'modules/worker-container-app.bicep' = {
  name: 'calc2-worker-container-app'
  params: {
    name: 'aspire-calc2-worker'
    location: location
    containerAppsEnvId: cae.outputs.id
    workloadProfileName: 'heavy-worker'      // D8: 8 vCPU / 32 GB per node
    registryServer: registryServer
    imageName: 'calc2-worker'
    imageTag: imageTag
    serviceBusNamespace: serviceBusNamespace
    queueName: 'calc2-jobs'
    cpu: '4'         // 4 vCPU per replica (half a D8 node per replica)
    memory: '8Gi'
    minReplicas: 0
    maxReplicas: 50
    messagesPerReplica: 5
    cpuScaleThreshold: 70
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output apiUrl string = 'https://${api.outputs.fqdn}'
output caeDomain string = cae.outputs.defaultDomain

// Principal IDs — used in post-deployment RBAC role assignments (see register-tasks.sh).
output apiPrincipalId string = api.outputs.principalId
output calc1WorkerPrincipalId string = calc1Worker.outputs.principalId
output calc2WorkerPrincipalId string = calc2Worker.outputs.principalId
