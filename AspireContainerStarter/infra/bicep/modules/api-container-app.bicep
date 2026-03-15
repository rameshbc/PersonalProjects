// ─────────────────────────────────────────────────────────────────────────────
// API Container App
//
// Workload profile : Consumption (serverless)
// Scaling          : HTTP concurrency (KEDA built-in)
//                    1–5 replicas; scale-up at 50 concurrent requests/replica
// Auth to ACR      : System-assigned managed identity (no credentials stored)
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Container Apps Environment resource ID.')
param containerAppsEnvId string

@description('ACR login server (e.g. myacr.azurecr.io).')
param registryServer string

@description('Image tag to deploy.')
param imageTag string

@description('Scale: max concurrent HTTP requests per replica before another replica is added.')
param concurrentRequestsPerReplica int = 50

@description('Scale: minimum replicas (keep ≥ 1 so there is no cold-start latency for users).')
param minReplicas int = 1

@description('Scale: maximum replicas.')
param maxReplicas int = 5

// ── Container App ─────────────────────────────────────────────────────────────

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'aspire-api'
  location: location
  identity: {
    type: 'SystemAssigned'   // Used for ACR pull; no stored credentials.
  }
  properties: {
    managedEnvironmentId: containerAppsEnvId
    workloadProfileName: 'Consumption'   // Serverless; no dedicated node pool.

    configuration: {
      ingress: {
        external: true          // Publicly reachable.
        targetPort: 8080        // ASP.NET Core listens on 8080 inside the container.
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: registryServer
          identity: 'system'   // Pull images using the system-assigned MI above.
        }
      ]
    }

    template: {
      containers: [
        {
          name: 'api'
          image: '${registryServer}/api:${imageTag}'
          resources: {
            cpu: json('0.5')   // Half a vCPU — adequate for I/O-bound HTTP work.
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
          ]
        }
      ]

      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                // Add a replica when any existing replica handles this many concurrent requests.
                concurrentRequests: string(concurrentRequestsPerReplica)
              }
            }
          }
        ]
      }
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output fqdn string = api.properties.configuration.ingress.fqdn
output principalId string = api.identity.principalId
output name string = api.name
