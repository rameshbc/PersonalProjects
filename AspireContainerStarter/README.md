# AspireContainerStarter

A production-grade .NET 10 + Aspire 13 distributed system validating enterprise patterns for
Azure-hosted workloads: Managed Identity auth throughout, adaptive Service Bus processing,
Redis caching, App Configuration, Key Vault, and Container Apps with workload-profile-based
compute grouping.

---

## Table of contents

- [Architecture](#architecture)
- [Solution structure](#solution-structure)
- [Tech stack](#tech-stack)
- [Prerequisites](#prerequisites)
- [Dev container / Codespaces](#dev-container--codespaces)
- [Local development](#local-development)
- [Infrastructure extensions](#infrastructure-extensions)
- [Configuration](#configuration)
- [Testing](#testing)
- [CI/CD](#cicd)
- [Deployment](#deployment)
- [Security scanning](#security-scanning)

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Clients                                                        │
└──────────────────────────┬──────────────────────────────────────┘
                           │ HTTPS
┌──────────────────────────▼──────────────────────────────────────┐
│  API  (Azure Container Apps — Consumption profile)              │
│  • Minimal API endpoints: POST /jobs/calc1  POST /jobs/calc2    │
│  • SignalR hub: /hubs/job-progress                              │
│  • Publishes job messages to Service Bus queues                 │
│  • Subscribes to job-progress topic for real-time updates       │
└──────┬───────────────────────┬──────────────────────────────────┘
       │ Service Bus           │ Service Bus
       │ calc1-jobs queue      │ calc2-jobs queue
┌──────▼──────────┐    ┌───────▼──────────┐
│  Calc1Worker    │    │  Calc2Worker     │
│  (D4 dedicated) │    │  (D8 dedicated)  │
│  standard-worker│    │  heavy-worker    │
│  profile        │    │  profile         │
│  0–50 replicas  │    │  0–50 replicas   │
│  KEDA: SB+CPU   │    │  KEDA: SB+CPU    │
└──────┬──────────┘    └───────┬──────────┘
       └──────────┬────────────┘
                  │ Service Bus  job-progress topic
                  ▼
         API (SignalR broadcast back to clients)

Shared resources (all via Managed Identity, no stored secrets):
  Azure SQL    →  EF Core + AzureAdTokenInterceptor + Polly retry
                  API reads jobs; workers persist CalculationResults rows
  Azure Redis  →  StackExchangeRedis + Entra ID token auth
  App Config   →  IHostApplicationBuilder.AddAzureAppConfigurationWithManagedIdentity()
  Key Vault    →  IHostApplicationBuilder.AddAzureKeyVaultWithManagedIdentity()
```

---

## Solution structure

```
AspireContainerStarter/
├── AspireContainerStarter.slnx          # Solution file (new XML format)
├── global.json                            # Pins .NET 10 SDK
├── Directory.Build.props                  # Shared: Nullable, ImplicitUsings, LangVersion=preview
│
├── src/
│   ├── AppHost/                           # Aspire orchestration + resource wiring
│   ├── ServiceDefaults/                   # OTel, health checks, service discovery
│   ├── Infrastructure/                    # NuGet library — MI auth extensions + Polly
│   ├── Contracts/                         # Shared message types (records)
│   ├── Api/                               # ASP.NET Core Minimal API + SignalR
│   └── Workers/
│       ├── Calc1Worker/                   # Service Bus consumer (standard compute)
│       └── Calc2Worker/                   # Service Bus consumer (heavy compute)
│
├── tests/
│   ├── AppHost.Tests/                     # Aspire integration tests
│   └── Infrastructure.Tests/             # Unit tests (15 passing)
│
└── infra/
    ├── bicep/                             # Container Apps + workload profiles
    │   ├── main.bicep
    │   ├── main.bicepparam
    │   └── modules/
    │       ├── container-apps-env.bicep   # Env with Consumption / D4 / D8 profiles
    │       ├── api-container-app.bicep    # HTTP scaling, Consumption profile
    │       └── worker-container-app.bicep # KEDA SB+CPU scaling, dedicated profile
    └── acr/                               # Vulnerability scanning
        ├── scan-task.yaml                 # ACR multi-step Task definition
        ├── scan.sh                        # IMDS token exchange + Trivy
        └── register-tasks.sh             # One-time setup script
```

---

## Tech stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10, C# (preview lang) |
| Orchestration | .NET Aspire 13.1.2 |
| API | ASP.NET Core Minimal API, SignalR |
| Workers | `Microsoft.Extensions.Hosting` worker service |
| Database | Azure SQL + EF Core 10 |
| Cache | Azure Cache for Redis (Entra ID auth) |
| Messaging | Azure Service Bus 7.18 |
| Configuration | Azure App Configuration 8 + Azure Key Vault |
| Identity | `DefaultAzureCredential` (MI in Azure, CLI/VS locally) |
| Resilience | Polly v8 via `Microsoft.Extensions.Resilience` 10.3 |
| Observability | OpenTelemetry — traces, metrics, logs → Aspire dashboard / OTLP |
| Hosting | Azure Container Apps (workload profiles) |
| Registry | Azure Container Registry |
| Security | Trivy + Microsoft Defender for Containers + ACR Tasks |

---

## Prerequisites

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.0.x | https://dot.net |
| Docker Desktop | latest | https://docker.com |
| Azure CLI | 2.60+ | `brew install azure-cli` |
| Aspire workload | 13.x | `dotnet workload install aspire` |

---

## Dev container / Codespaces

The repo ships a fully configured dev container (`.devcontainer/`) so you can start
coding without installing anything locally.

### What's included

| Feature | Detail |
|---|---|
| Base image | `mcr.microsoft.com/devcontainers/dotnet:1-10.0-noble` (.NET 10, Ubuntu 24.04) |
| Docker-in-Docker | Required for Aspire to spin up SQL Server, Redis, and the Service Bus Emulator |
| Azure CLI + Bicep | Pre-installed for `az login` and Bicep deployments |
| Azure Developer CLI (`azd`) | Pre-installed for Container Apps deployments |
| GitHub CLI | Pre-installed for PR / Actions workflows |
| VS Code extensions | C# Dev Kit, Docker, Azure tools, GitLens, EditorConfig |
| Forwarded ports | `18888` (Aspire dashboard), `5000`/`5001` (API) |
| Post-create | NuGet restore + HTTPS dev-cert generated automatically |

> **Disk requirement:** The dev container requests **100 GB** of storage to accommodate
> the .NET 10 SDK, NuGet package cache, and Docker-in-Docker images.
> GitHub Codespaces: choose a machine with at least 32 GB RAM / 64 GB storage for a
> comfortable experience with all Aspire containers running.

### Option A — GitHub Codespaces (zero local setup)

1. Click **Code → Codespaces → Create codespace on main** in the GitHub UI.
2. Wait for the container to build and the `postCreateCommand` to finish (~3–5 min
   on first launch; subsequent opens reuse the cached image).
3. Open a terminal and run the AppHost:

   ```bash
   dotnet run --project src/AppHost/AspireContainerStarter.AppHost
   ```

4. VS Code will prompt to open the forwarded port. Visit **port 18888** for the
   Aspire dashboard.

### Option B — VS Code Dev Containers (local Docker)

**Prerequisites:** Docker Desktop running locally, VS Code with the
[Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers).

1. Open the repo folder in VS Code.
2. When prompted _"Reopen in Container"_, click it — or run
   **Dev Containers: Reopen in Container** from the command palette.
3. The container builds, features install, and `postCreateCommand` runs automatically.
4. Run the AppHost as above.

### NuGet package cache (performance)

The `postCreateCommand` symlinks the NuGet cache from the workspace volume into the
container user's home directory:

```
/workspaces/AspireContainerStarter/.nuget/packages  ←→  ~/.nuget/packages
```

This means NuGet packages survive container rebuilds as long as the Codespace / volume
is preserved, keeping restores fast on subsequent opens.

---

## Local development

### 1 — Clone and restore

```bash
git clone https://github.com/<org>/AspireContainerStarter.git
cd AspireContainerStarter
dotnet restore AspireContainerStarter.slnx
```

### 2 — Run via Aspire AppHost

```bash
dotnet run --project src/AppHost/AspireContainerStarter.AppHost
```

The Aspire dashboard opens at **http://localhost:18888**.

Locally, Aspire starts these Docker containers automatically (all persistent across restarts):
- **SQL Server** — port 1433
- **Redis** — port 6379
- **Azure Service Bus Emulator** — port 5672 (AMD64 image; requires Rosetta 2 on Apple Silicon via Docker Desktop)

No user secrets are needed for any of these — Aspire injects the connection strings
automatically via `WithReference()`. The Service Bus emulator config (queues/topic/subscription)
is generated by Aspire from the AppHost declarations.

### 3 — User secrets (optional — only for real Azure services locally)

All local-dev dependencies (SQL Server, Redis, Service Bus) start automatically via Aspire
and require no manual configuration. User secrets are only needed if you want to point at
real Azure services instead of the local Docker containers:

```bash
# Point at a real Azure Service Bus namespace instead of the emulator
cd src/Api/AspireContainerStarter.Api
dotnet user-secrets set "ConnectionStrings:service-bus" "my-ns.servicebus.windows.net"
```

App Configuration and Key Vault are always skipped locally (graceful no-op when the
connection string is absent). To test against real Azure instances:

```bash
dotnet user-secrets set "ConnectionStrings:app-config" "https://my-store.azconfig.io"
dotnet user-secrets set "ConnectionStrings:key-vault"  "https://my-vault.vault.azure.net/"
```

---

## Infrastructure extensions

The `AspireContainerStarter.Infrastructure` NuGet library provides Managed Identity
auth wrappers for all Azure services. Call them in each service's `Program.cs`
**before** `builder.Build()`.

### Azure SQL (EF Core)

```csharp
builder.Services.AddAzureSqlWithManagedIdentity<MyDbContext>(
    connectionString: builder.Configuration.GetConnectionString("SqlDb")!);
```

Includes: `AzureAdTokenInterceptor`, keyed Polly pipeline `"azure-sql"`, health check.

Workers detect local vs Azure automatically — local Docker SQL containers use plain SQL auth;
Azure uses Managed Identity. To run EF migrations on startup, register the migrator **before**
the Service Bus processor:

```csharp
// Local dev (localhost connection string) — plain SQL auth
builder.Services.AddDbContext<CalculationDbContext>(o => o.UseSqlServer(sqlCs));
// Azure — MI auth
builder.Services.AddAzureSqlWithManagedIdentity<CalculationDbContext>(sqlCs);

// Run EF migrations on startup, before the message processor begins
builder.Services.AddHostedService<DbMigratorHostedService<CalculationDbContext>>();
builder.Services.AddHostedService<ServiceBusProcessorHostedService<Calc1JobMessage>>();
```

The `DbMigratorHostedService<TContext>` is included in the Infrastructure library and calls
`context.Database.MigrateAsync()` in `StartAsync`, ensuring the schema is up to date before
any messages are consumed.

### Azure Service Bus — publisher

```csharp
builder.Services.AddAzureServiceBusPublisherWithManagedIdentity(
    fullyQualifiedNamespace: builder.Configuration["ConnectionStrings:service-bus"]!,
    queueOrTopicName: "my-queue");
```

### Azure Service Bus — consumer

```csharp
builder.Services.AddAzureServiceBusConsumerWithManagedIdentity<MyMessage, MyHandler>(
    fullyQualifiedNamespace: builder.Configuration["ConnectionStrings:service-bus"]!,
    queueName: "my-queue");
```

> **Connection string vs namespace**: The `fullyQualifiedNamespace` parameter accepts
> both formats. Aspire automatically injects the right value per environment:
> - **Local dev (emulator)**: full connection string (`Endpoint=sb://localhost;...`) —
>   the library detects this and uses SAS auth with AMQP-TCP transport.
> - **Azure (publish mode)**: namespace FQDN (`my-ns.servicebus.windows.net`) —
>   the library uses `DefaultAzureCredential` with AMQP-WebSockets transport.
> No code changes are needed between environments.

### Azure Service Bus — adaptive consumer (in-process concurrency scaling)

```csharp
builder.Services.AddAdaptiveAzureServiceBusConsumerWithManagedIdentity<MyMessage, MyHandler>(
    fullyQualifiedNamespace: "...",
    queueName: "my-queue",
    configureConcurrency: opts =>
    {
        opts.MinConcurrency    = 2;
        opts.MaxConcurrency    = 20;
        opts.GrowthThreshold   = 10;   // msgs/sec before scaling up
    });
```

### Azure Redis Cache

```csharp
builder.Services.AddAzureRedisCacheWithManagedIdentity(
    redisHostName: builder.Configuration["ConnectionStrings:redis-cache"]!);
```

### Azure App Configuration

```csharp
// No-op locally when connection string is absent (graceful fallback to appsettings).
builder.AddAzureAppConfigurationWithManagedIdentity(
    sentinelKey: "sentinel",          // refresh all settings when this key changes
    cacheExpiration: TimeSpan.FromSeconds(30),
    label: "production");             // load keys tagged "production"
```

### Azure Key Vault

```csharp
// No-op locally when connection string is absent.
builder.AddAzureKeyVaultWithManagedIdentity(
    reloadInterval: TimeSpan.FromHours(1));   // reload secrets periodically
```

> **Key Vault naming convention:** secret `ConnectionStrings--Db` maps to config key
> `ConnectionStrings:Db` (double-dash → colon).

---

## Configuration

Configuration is layered (last wins):

| Source | Environment | Notes |
|---|---|---|
| `appsettings.json` | All | Defaults |
| `appsettings.{env}.json` | Matching env | Dev overrides |
| User secrets | Local | `.NET user-secrets` |
| Azure App Configuration | Azure | Feature flags, shared settings |
| Azure Key Vault | Azure | Secrets (connection strings, API keys) |
| Container Apps env vars | Azure | Injected by Aspire `WithReference()` / `WithEnvironment()` |

### Required secrets (GitHub Actions)

| Secret | Value |
|---|---|
| `AZURE_CLIENT_ID` | Service principal / federated identity client ID |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |
| `AZURE_CONTAINER_REGISTRY` | ACR short name (e.g. `aspireprodacr`) |
| `AZURE_RESOURCE_GROUP` | Resource group name |

---

## Testing

```bash
# Unit tests (Infrastructure library — 15 tests, no Azure dependencies)
dotnet test tests/AspireContainerStarter.Infrastructure.Tests \
  --configuration Release --logger trx

# Aspire integration tests (requires Docker)
dotnet test tests/AspireContainerStarter.AppHost.Tests \
  --configuration Release
```

---

## CI/CD

### CI — `.github/workflows/ci.yml`

Triggers on every push and PR to `main` / `develop`.

| Job | Steps |
|---|---|
| `build-and-test` | restore → build → unit tests → upload results |
| `lint` | `dotnet format --verify-no-changes` |

### CD — `.github/workflows/cd.yml`

Triggers on push to `main`. Runs in parallel across three services
(`api`, `calc1-worker`, `calc2-worker`).

```
restore → build to local daemon → Trivy scan → push to ACR → az containerapp update
                                       │
                             fails on CRITICAL/HIGH
                             SARIF → GitHub Security tab
```

OIDC authentication — no stored Azure credentials.

### NuGet publish — `.github/workflows/nuget-publish.yml`

Triggers on tags matching `infra/v*`. Publishes `Infrastructure` library to GitHub Packages.

---

## Deployment

### First-time infrastructure setup

```bash
# 1. Login
az login
az account set --subscription <subscription-id>

# 2. Create resource group (if needed)
az group create --name <rg> --location australiaeast

# 3. Deploy Container Apps environment + all Container Apps
az deployment group create \
  --resource-group <rg> \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.bicepparam \
  --parameters imageTag=<git-sha>

# 4. Set up ACR scanning + Defender for Containers
./infra/acr/register-tasks.sh \
  <acr-name> <rg> <github-pat> <org/repo>
```

After step 3, the Bicep output includes principal IDs for each Container App.
Use those to complete the RBAC assignments in `register-tasks.sh` (the commented block).

### Subsequent deployments

The CD workflow handles these automatically on every push to `main`. To deploy
manually with a specific SHA:

```bash
az deployment group create \
  --resource-group <rg> \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/main.bicepparam \
  --parameters imageTag=<git-sha>
```

### Workload profile reference

| Profile | SKU | vCPU/node | RAM/node | Used by |
|---|---|---|---|---|
| `Consumption` | Shared | — | — | API |
| `standard-worker` | D4 | 4 | 16 GB | Calc1Worker |
| `heavy-worker` | D8 | 8 | 32 GB | Calc2Worker |

To add a heavier tier (e.g. D16), add a profile in `container-apps-env.bicep` and
pass the new name to the worker module's `workloadProfileName` parameter.

---

## Security scanning

Three scanning layers are active:

### Layer 1 — Pre-push (GitHub Actions, blocking gate)

Every CD run builds the Docker image to the **local runner daemon** and scans it with
**Trivy** before anything is pushed to ACR.

- Blocks the push on **CRITICAL or HIGH** findings that have a fix available
- SARIF results uploaded to **GitHub Security → Code scanning** tab
- Uses `ignore-unfixed: true` to suppress CVEs with no upstream fix

### Layer 2 — On-push (Microsoft Defender for Containers)

Enabled by `register-tasks.sh`. Every image pushed to ACR is automatically scanned.
Findings appear in **Microsoft Defender for Cloud → Recommendations**.

### Layer 3 — Scheduled (ACR Task, daily)

An ACR multi-step Task runs daily at **02:00 UTC**. It authenticates to ACR using the
task's managed identity (IMDS token exchange → ACR refresh token), then runs
Trivy across all service images. This catches vulnerability drift on images that
haven't been recently pushed.

```bash
# Trigger manually
az acr task run --name vulnerability-scan --registry <acr-name>

# View logs
az acr task logs --name vulnerability-scan --registry <acr-name>
```

See [`infra/README.md`](infra/README.md) for the full deployment and scanning reference.
