// ─────────────────────────────────────────────────────────────────────────────
// Worker Container App — reusable module
//
// Scaling strategy (two complementary rules):
//
//   1. Service Bus queue depth (primary)
//      KEDA azure-servicebus trigger — authenticated via the Container App's
//      system-assigned managed identity (identity: system).
//      Adds one replica per `messagesPerReplica` messages in the queue.
//      Activates from zero when ≥ 1 message appears (activationMessageCount).
//
//   2. CPU utilisation (secondary)
//      If replicas are already running and CPU spikes above `cpuScaleThreshold`%,
//      KEDA adds further replicas even if the queue depth rule is satisfied.
//      This handles CPU-heavy computation that backs up within a single replica.
//
// Workload profile (dedicated):
//   Pass `workloadProfileName` = 'standard-worker' (D4) or 'heavy-worker' (D8)
//   and matching `cpu`/`memory` values per the table below:
//
//   Profile          | cpu  | memory | Use-case
//   standard-worker  | '2'  | '4Gi'  | Calc1Worker — moderate compute
//   heavy-worker     | '4'  | '8Gi'  | Calc2Worker — intensive compute
// ─────────────────────────────────────────────────────────────────────────────

@description('Container App name.')
param name string

@description('Azure region.')
param location string = resourceGroup().location

@description('Container Apps Environment resource ID.')
param containerAppsEnvId string

@description('Workload profile name — must match a profile defined in the environment.')
@allowed(['standard-worker', 'heavy-worker'])
param workloadProfileName string

@description('ACR login server (e.g. myacr.azurecr.io).')
param registryServer string

@description('Image name in ACR (e.g. calc1-worker).')
param imageName string

@description('Image tag to deploy.')
param imageTag string

// ── Service Bus KEDA parameters ───────────────────────────────────────────────

@description('Service Bus fully-qualified namespace (e.g. my-ns.servicebus.windows.net).')
param serviceBusNamespace string

@description('Queue name to consume from.')
param queueName string

@description('Messages per replica before KEDA adds another replica.')
param messagesPerReplica int = 5

// ── Resource allocation ───────────────────────────────────────────────────────

@description('vCPU allocation per replica. Must be within the chosen workload profile limits.')
param cpu string = '2'

@description('Memory allocation per replica (e.g. "4Gi").')
param memory string = '4Gi'

// ── Scale bounds ──────────────────────────────────────────────────────────────

@description('Minimum replicas. 0 enables scale-to-zero (idle workers cost nothing).')
@minValue(0)
param minReplicas int = 0

@description('Maximum replicas.')
@minValue(1)
param maxReplicas int = 50

@description('CPU utilisation % that triggers adding another replica (secondary scale rule).')
@minValue(1)
@maxValue(100)
param cpuScaleThreshold int = 70

// ── Container App ─────────────────────────────────────────────────────────────

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: 'SystemAssigned'   // Used for ACR pull AND Service Bus KEDA auth.
  }
  properties: {
    managedEnvironmentId: containerAppsEnvId
    workloadProfileName: workloadProfileName   // Dedicated compute pool.

    configuration: {
      registries: [
        {
          server: registryServer
          identity: 'system'
        }
      ]
    }

    template: {
      containers: [
        {
          name: name
          image: '${registryServer}/${imageName}:${imageTag}'
          resources: {
            cpu: json(cpu)     // e.g. json('2') → 2.0
            memory: memory
          }
          env: [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
          ]
        }
      ]

      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          // ── Primary: scale by Service Bus queue depth ───────────────────
          // KEDA authenticates to Service Bus using the Container App's
          // system-assigned managed identity — no connection strings needed.
          // Prerequisite: grant this app's principalId the role
          //   "Azure Service Bus Data Receiver" on the Service Bus namespace.
          {
            name: 'servicebus-queue-depth'
            custom: {
              type: 'azure-servicebus'
              metadata: {
                namespace: serviceBusNamespace
                queueName: queueName
                // Scale out: one replica per N messages
                messageCount: string(messagesPerReplica)
                // Scale to zero: only start replicas once ≥ 1 message exists
                activationMessageCount: '1'
              }
              // 'system' = use this Container App's system-assigned MI
              identity: 'system'
            }
          }

          // ── Secondary: CPU utilisation scale-up ─────────────────────────
          // Fires when existing replicas are CPU-saturated (e.g. heavy
          // serialisation or compute-intensive inner loops), adding more
          // replicas even if the queue rule is already satisfied.
          {
            name: 'cpu-utilisation'
            custom: {
              type: 'cpu'
              metadata: {
                type: 'Utilization'
                value: string(cpuScaleThreshold)
              }
            }
          }
        ]
      }
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output principalId string = worker.identity.principalId
output name string = worker.name
