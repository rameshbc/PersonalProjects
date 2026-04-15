# SharePoint Document Manager with Scaler

A production-ready enterprise document management system for SharePoint Online and SharePoint Embedded, built with **.NET 8**, **Azure Managed Identity**, and **Microsoft Graph API**.

## 🎯 Features

- **Dual-Platform Support**: SharePoint Online (CSOM → Graph API) and SharePoint Embedded
- **Managed Identity Authentication**: Secure, credential-less auth to Graph API using Azure-managed identities
- **Multi-Tenant Architecture**: Supports thousands of clients with isolated concurrency per client
- **Parallel Document Upload**: Bounded-channel producer/consumer with per-client bulkhead isolation
- **Resilience at Scale**: Polly v8 pipelines with tier-based retry strategies (Standard/Gold)
- **Graph API Optimization**: Batch requests ($batch), resumable sessions (5MB chunks), throttle-aware retry
- **Role-Based Folder Hierarchy**: Admin/Contributor/Reader roles enforced at folder level with break-inheritance
- **Excel Workbook Integration**: Read/write ranges via Graph /workbook session API
- **Real-Time Progress**: SignalR for upload progress updates to browser
- **Admin Portal**: Blazor Server portal for site provisioning and permission management
- **Audit Trail**: Append-only logs for compliance tracking
- **Docker Support**: Multi-stage builds for local dev and production
- **Azure Bicep IaC**: One-command infrastructure deployment
- **GitHub Actions CI/CD**: Automated build, test, and deployment pipeline
- **One-Time Migration Scripts**: PowerShell scripts for legacy CSOM → Graph migration

## 📋 Prerequisites

### Development
- **.NET 8 SDK** ([download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0))
- **Docker Desktop** ([download](https://www.docker.com/products/docker-desktop))
- **Azure CLI** ([download](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli))
- **Visual Studio Code** or **Visual Studio 2022**

### Production
- **Azure Subscription** with appropriate permissions
- **Azure Container Registry** (ACR) for Docker images
- **Azure App Service Plan** (B2/B3 SKU recommended)
- **Azure SQL Database** (Standard/Premium tier recommended)
- **Entra ID Application Registration** with Graph API permissions

## 🚀 Quick Start

### 1. Clone and Build

```bash
git clone <repository>
cd SharepointDocumentManagerWorkerWithScaler

# Restore and build
dotnet restore
dotnet build --configuration Release
```

### 2. Local Development with Docker Compose

```bash
# Copy dev environment template
cp .env.template .env.local

# Edit with your Graph API credentials
# AZURE_TENANT_ID=your-tenant-id
# AZURE_CLIENT_ID=your-client-id
# AZURE_CLIENT_SECRET=your-client-secret

# Start all services (SQL, API, Worker, Admin)
docker-compose up --build

# Access:
# - API:        http://localhost:7001/swagger
# - Admin:      http://localhost:7002
# - SQL Server: localhost,1433 (User: sa, Password: Dev_Password123!)
```

### 3. Database Migrations

```bash
# Apply EF Core migrations to LocalDB or Docker SQL Server
dotnet ef database update \
  --project src/SharepointDocManager.Infrastructure \
  --startup-project src/SharepointDocManager.Api
```

### 4. Run Services Locally

```bash
# Terminal 1: API
cd src/SharepointDocManager.Api
dotnet run --configuration Release

# Terminal 2: Worker
cd src/SharepointDocManager.Worker
dotnet run --configuration Release

# Terminal 3: Admin Portal
cd src/SharepointDocManager.Admin
dotnet run --configuration Release
```

## 📚 Project Structure

```
SharepointDocumentManagerWorkerWithScaler/
├── src/
│   ├── SharepointDocManager.Core/              # Domain models, entities, interfaces
│   ├── SharepointDocManager.Infrastructure/    # Graph API, EF Core, adapters, resilience
│   ├── SharepointDocManager.Application/       # Services, commands, queries, handlers
│   ├── SharepointDocManager.Api/               # ASP.NET Core REST API + SignalR
│   ├── SharepointDocManager.Admin/             # Blazor Server admin portal
│   └── SharepointDocManager.Worker/            # Background services (upload, sync)
├── deploy/
│   ├── docker/                                 # Dockerfiles + docker-compose
│   ├── infrastructure/                         # Bicep IaC templates + modules
│   └── scripts/                                # Deployment scripts (PowerShell)
├── tests/                                      # Unit & integration tests
├── docs/
│   ├── ARCHITECTURE.md                         # Detailed design documentation
│   ├── DEPLOYMENT.md                           # Production deployment guide
│   └── DEVELOPMENT.md                          # Local dev guide
└── README.md                                   # This file
```

## 🏗️ Architecture Overview

### Authentication
- **Managed Identity**: User-assigned MI for app-only Graph API access
- **Role-Based Access**: Entra ID groups per client + role (e.g., `client-001-Admin`)
- **Folder-Level Permissions**: Break inheritance, apply role groups, protect sensitive folders

### Adapter Pattern
- **IDocumentStorageAdapter**: Single abstraction for SharePoint Online & Embedded
- **StorageAdapterResolver**: ONE dispatch point (no branching logic throughout codebase)
- Keyed DI: `GetRequiredKeyedService<IDocumentStorageAdapter>("SP")` or `("SPE")`

### Parallel Processing
- **Bounded Channel**: Producer (API) → Consumer (Worker), backpressure prevents OOM
- **Per-Client Bulkhead**: SemaphoreSlim per client prevents thundering herd
- **Polly Resilience**: Standard (6 retries) & Gold (10 retries) pipelines
- **Graph Throttling Handler**: Respects `Retry-After` header on 429/503

### Data Flow
```
API POST /batch/upload
  ↓ (SignalR group for client)
Bounded Channel<UploadRequest>
  ↓ (N concurrent consumers)
BatchUploadWorker
  ↓ (orchestrates parallel uploads)
DocumentOrchestrationService
  ↓
GraphBatchExecutor (20 requests/batch)
  ↓
Graph API (with retry + throttle awareness)
  ↓
SharePoint (Online or Embedded)
  ↓ (upload complete)
Signal progress → Browser (real-time)
Store audit log → SQL Backend
```

## 📖 Documentation

- **[ARCHITECTURE.md](docs/ARCHITECTURE.md)** — Design patterns, authentication, adapter pattern, resilience strategies
- **[DEPLOYMENT.md](docs/DEPLOYMENT.md)** — Production deployment, Azure Bicep, GitHub Actions, RBACs
- **[DEVELOPMENT.md](docs/DEVELOPMENT.md)** — Local development, debugging, testing

## 🔄 CI/CD Pipeline

### GitHub Actions Workflow (`.github/workflows/deploy.yml`)

1. **Build & Test**
   - Restore NuGet dependencies
   - Build Release configuration
   - Run unit tests

2. **Build Docker Images**
   - Build multi-stage Docker images for API, Worker, Admin
   - Push to Azure Container Registry (ACR)

3. **Deploy Infrastructure**
   - Deploy Bicep templates to Azure
   - Create App Services, SQL Database, Key Vault, Managed Identity
   - Configure networking and RBAC

4. **Deploy Services**
   - Update App Service Docker images
   - Run EF Core database migrations
   - Verify health checks

### Manual Deployment

```bash
# One-command production deployment
.\deploy\scripts\Deploy-Azure.ps1 `
  -SubscriptionId "00000000-0000-0000-0000-000000000000" `
  -Environment prod `
  -Location eastus `
  -AcrName "your-acr-name"
```

## 🔐 Secrets Management

### Development
- Create `.env.local` with:
  ```
  AZURE_TENANT_ID=your-tenant-id
  AZURE_CLIENT_ID=your-client-id
  AZURE_CLIENT_SECRET=your-client-secret
  ```
- Use `docker-compose.yml` for local SQL Server

### Production
- Stored in **Azure Key Vault**
- Accessed via **Managed Identity** (no secrets in code/config)
- GitHub Actions secrets for CI/CD:
  - `AZURE_TENANT_ID`
  - `AZURE_CLIENT_ID`
  - `AZURE_CLIENT_SECRET`
  - `AZURE_SUBSCRIPTION_ID`
  - `SQL_ADMIN_USERNAME` / `SQL_ADMIN_PASSWORD`

## 📊 Monitoring & Logging

### Application Insights
- Request/dependency tracking
- Exception telemetry
- Performance metrics (CPU, memory, response time)
- Custom events (upload started, permission sync completed)

### Structured Logging
- Serilog with Compact JSON formatter
- Includes correlation IDs and client context
- Log levels: Error, Warning, Information, Debug

### Health Checks
- `/health` endpoint for liveness probes
- Checked by App Service health check settings
- Used by orchestration tools (AKS, App Service)

## 🚀 Performance Considerations

| Scenario | Throughput | Details |
|----------|-----------|---------|
| Single file upload | ~10 MB/s | Typical network + Graph API latency |
| Batch (20 files) | 150+ MB/s | Parallel consumers, bulkhead isolation |
| 1000 clients concurrent | Scales linearly | Per-client SemaphoreSlim + Polly bulkhead |
| Large file (>500 MB) | Resumable | 5MB chunks, session-based retry |
| Permission sync (1000 items) | Delta queries | 1 API call per 100 items with incremental sync |

## 🔗 API Endpoints

### Documents
- `GET /api/clients/{clientId}/folders/{folderId}/documents` — List documents
- `POST /api/clients/{clientId}/documents/upload` — Single file upload
- `POST /api/clients/{clientId}/documents/batch` — Batch upload with progress

### Folders
- `POST /api/clients/{clientId}/folders` — Create folder
- `POST /api/clients/{clientId}/folders/{folderId}/permissions` — Grant permissions

### Admin
- `GET /api/admin/clients` — List all clients
- `POST /api/admin/clients/provision` — Provision new client
- `PATCH /api/admin/clients/{clientId}/storage-backend` — Switch SP/SPE

### Health
- `GET /health` — Health check (liveness probe)

## 🧪 Testing

```bash
# Unit tests
dotnet test --configuration Release --filter "FullyQualifiedName~Unit"

# Integration tests (requires SQL Server)
dotnet test --configuration Release --filter "FullyQualifiedName~Integration"

# Code coverage
dotnet test --configuration Release /p:CollectCoverage=true
```

## 🔄 Migration Scripts

### For Existing SharePoint Online Sites

**Script A**: Grant Managed Identity permissions to existing sites
```powershell
.\deploy\scripts\migration\ScriptA-GrantMIPermissions\Grant-ManagedIdentityPermissions.ps1 `
  -ConfigPath ./clients.csv `
  -TenantId "your-tenant-id" `
  -ManagedIdentityObjectId "your-mi-object-id" `
  -DryRun:$false
```

### For SP → SPE Migration

**Script B**: Migrate content from SharePoint Online to SharePoint Embedded
```powershell
.\deploy\scripts\migration\ScriptB-MigrateSpToSpe\Migrate-SpToSpe.ps1 `
  -ConfigPath ./migration-config.json `
  -DryRun:$false -Verbose
```

See [Migration Guide](docs/DEPLOYMENT.md#migration) for details.

## 📝 Environment Variables

### API / Worker / Admin

| Variable | Purpose | Example |
|----------|---------|---------|
| `ASPNETCORE_ENVIRONMENT` | Env (Development/Staging/Production) | `Production` |
| `ConnectionStrings__DefaultConnection` | SQL Server connection | `Server=tcp:server.database.windows.net...` |
| `AZURE_TENANT_ID` | Entra ID tenant for Graph | `00000000-0000-0000-0000-000000000000` |
| `AZURE_CLIENT_ID` | Managed identity client ID | `00000000-0000-0000-0000-000000000001` |
| `AZURE_CLIENT_SECRET` | Client secret (if not using MI) | (avoid in production) |
| `Graph__AuthMode` | Auth strategy | `ManagedIdentity` or `ClientCredentials` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights ingestion | `InstrumentationKey=...` |

## 🆘 Troubleshooting

### API won't start: "Connection timeout"
- Verify SQL Server is running: `docker ps`
- Check connection string in appsettings.json
- Ensure firewall allows localhost:1433

### Graph API returns 403 Unauthorized
- Verify Managed Identity has Graph API permissions
- Check Azure Portal > App Registrations > API Permissions > Grant Admin Consent
- Ensure Client ID matches app registration

### Upload fails with 429 Throttling
- Expected behavior, retry logic handles it
- Check Polly pipeline configuration in Program.cs
- Monitor Graph throttling metrics in Application Insights

### Database migration fails in Bicep deployment
- Ensure EF Core Design package is installed
- Check SQL admin credentials are correct
- Verify firewall allows Azure IP ranges

## 📚 Additional Resources

- [Microsoft Graph API Documentation](https://docs.microsoft.com/en-us/graph/api/overview)
- [Azure Managed Identity](https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/)
- [Bicep Language Reference](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/)
- [Polly Resilience Library](https://github.com/App-vNext/Polly)
- [Blazor Server Documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/?view=aspnetcore-8.0)

## 👥 Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`
3. Commit changes: `git commit -m 'Add amazing feature'`
4. Push to branch: `git push origin feature/amazing-feature`
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see [LICENSE](LICENSE) file for details.

## 🤝 Support

For issues, questions, or suggestions:
1. Check [ARCHITECTURE.md](docs/ARCHITECTURE.md) for design context
2. Review [DEPLOYMENT.md](docs/DEPLOYMENT.md) for deployment issues
3. Open a GitHub issue with detailed logs
4. Include relevant sections from Application Insights telemetry

---

**Last Updated**: April 7, 2026
**Version**: 1.0.0 (Production Ready)
