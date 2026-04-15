# Local Development Guide

Complete guide for setting up and developing SharePoint Document Manager locally.

## Prerequisites

### Required
- **.NET 8 SDK** ([download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0))
  ```bash
  dotnet --version  # should be 8.x.x
  ```

- **Docker Desktop** ([download](https://www.docker.com/products/docker-desktop))
  ```bash
  docker --version
  docker-compose --version
  ```

- **SQL Server 2022** (via Docker or local installation)
  - Docker Compose includes SQL Server 2022 automatically

### Optional but Recommended
- **Visual Studio 2022** Professional+ or **Visual Studio Code**
- **Azure CLI** ([download](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli))
- **Git** for version control
- **Postman** or **REST Client** extension for testing APIs
- **SQL Server Management Studio** for database debugging

## Step 1: Clone and Setup

```bash
# Clone repository
git clone https://github.com/your-org/SharepointDocumentManagerWorkerWithScaler.git
cd SharepointDocumentManagerWorkerWithScaler

# Restore dependencies
dotnet restore

# Install Entity Framework Core tools
dotnet tool install --global dotnet-ef
dotnet tool install --global dotnet-aspire
```

## Step 2: Configure Local Environment

### 2.1 Create `.env.local` File

Create `.env.local` in the project root:

```bash
# Graph API credentials (from Entra ID App Registration)
AZURE_TENANT_ID=your-entra-tenant-id
AZURE_CLIENT_ID=your-entra-app-id
AZURE_CLIENT_SECRET=your-entra-app-secret

# Auth mode: choose one
# - ManagedIdentity (production, requires Azure login)
# - ClientCredentials (uses Client ID + Secret from above)
# - Interactive (opens browser for sign-in)
GRAPH_AUTH_MODE=ClientCredentials

# SQL Server (Docker Compose will provide this)
SQL_SERVER=localhost,1433
SQL_DATABASE=SharepointDocManager
SQL_USERNAME=sa
SQL_PASSWORD=Dev_Password123!

# Optional: Application Insights
APPLICATIONINSIGHTS_CONNECTION_STRING=
```

### 2.2 Update `appsettings.Development.json`

Files automatically configured:
- `src/SharepointDocManager.Api/appsettings.Development.json`
- `src/SharepointDocManager.Worker/appsettings.Development.json`
- `src/SharepointDocManager.Admin/appsettings.Development.json`

Ensure local connection strings point to Docker SQL:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=SharepointDocManager;User Id=sa;Password=Dev_Password123!;TrustServerCertificate=True"
  },
  "Graph": {
    "AuthMode": "ClientCredentials"
  }
}
```

## Step 3: Start Local Services with Docker Compose

### 3.1 Start All Services

```bash
# Build and start all services
docker-compose up --build

# Or run in detached mode
docker-compose up -d --build
```

This starts:
- **SQL Server 2022** on port 1433 (User: sa, Password: Dev_Password123!)
- **API** on port 7001 (http://localhost:7001)
- **Admin Portal** on port 7002 (http://localhost:7002)
- **Worker** (background service)

### 3.2 View Logs

```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f api
docker-compose logs -f worker
docker-compose logs -f admin
docker-compose logs -f mssql

# Follow new entries
docker-compose logs -f --tail=50
```

### 3.3 Stop Services

```bash
# Stop all services
docker-compose down

# Stop and remove volumes (delete databases)
docker-compose down -v

# Stop specific service
docker-compose stop api
docker-compose restart worker
```

## Step 4: Database Migrations

### 4.1 Apply Initial Migrations

```bash
# Create database and apply all migrations
dotnet ef database update `
  --project src/SharepointDocManager.Infrastructure `
  --startup-project src/SharepointDocManager.Api `
  --configuration Debug
```

### 4.2 Create New Migration (After Model Changes)

```bash
# Generate migration from changes
dotnet ef migrations add AddNewFeatureXYZ `
  --project src/SharepointDocManager.Infrastructure `
  --startup-project src/SharepointDocManager.Api `
  --configuration Debug

# Review generated migration in Persistence/Migrations/
# Then apply it:
dotnet ef database update
```

### 4.3 Reset Database to Clean State

```bash
# WARNING: Deletes all data!
dotnet ef database drop --force `
  --project src/SharepointDocManager.Infrastructure `
  --startup-project src/SharepointDocManager.Api

# Re-apply migrations
dotnet ef database update
```

## Step 5: Run Services Locally (Without Docker)

Use this for debugging or if Docker is unavailable.

### 5.1 Start SQL Server (if not using Docker)

```bash
# Option A: Use Docker Compose for SQL only
docker-compose up -d mssql

# Option B: Use LocalDB (Windows only)
sqllocaldb create LocalDev
sqllocaldb start LocalDev
# Update connection string to: (localdb)\LocalDev
```

### 5.2 Terminal 1: API Service

```bash
cd src/SharepointDocManager.Api
dotnet run --configuration Debug
# API starts on https://localhost:7001
# Swagger UI: https://localhost:7001/swagger
```

### 5.3 Terminal 2: Worker Service

```bash
cd src/SharepointDocManager.Worker
dotnet run --configuration Debug
# Worker logs to console, watch for processing status
```

### 5.4 Terminal 3: Admin Portal

```bash
cd src/SharepointDocManager.Admin
dotnet run --configuration Debug
# Admin Portal starts on https://localhost:7002
```

## Step 6: Test the Application

### 6.1 API Endpoints (Swagger UI)

Open https://localhost:7001/swagger in your browser after starting the API.

### 6.2 Upload a Test Document

```bash
# Using PowerShell
$file = "C:\path\to\test-document.pdf"
$clientId = "test-client-001"
$folderId = "root"

$response = Invoke-RestMethod `
  -Uri "https://localhost:7001/api/clients/$clientId/documents/upload" `
  -Method Post `
  -InFile $file `
  -Headers @{
    "Content-Type" = "application/octet-stream"
  } `
  -SkipCertificateCheck

$response | ConvertTo-Json
```

### 6.3 Test Batch Upload with Progress

```bash
# Using curl (requires multiple files)
curl -X POST https://localhost:7001/api/clients/test-client/documents/batch `
  -H "Content-Type: multipart/form-data" `
  -F "files=@file1.pdf" `
  -F "files=@file2.pdf" `
  -F "files=@file3.pdf" `
  -k  # Skip cert verification for localhost
```

### 6.4 Admin Portal

Navigate to https://localhost:7002 after starting Admin service.

Features:
- View configured clients
- Provision new client sites
- Manage folder permissions
- Toggle storage backend (SP ↔ SPE)

### 6.5 View Database

Use SQL Management Studio or Azure Data Studio:

```
Server: localhost,1433
Database: SharepointDocManager
Username: sa
Password: Dev_Password123!
TrustServerCertificate: True
```

Query audit logs:
```sql
SELECT TOP 100 * FROM AuditLogs ORDER BY Timestamp DESC
SELECT * FROM ClientSites WHERE ClientId = 'test-client-001'
```

## Step 7: Debugging

### 7.1 VS Code Debugging

1. Install **C# Dev Kit** extension
2. Create `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "API (Debug)",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/src/SharepointDocManager.Api/bin/Debug/net8.0/SharepointDocManager.Api.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/SharepointDocManager.Api",
      "stopAtEntry": false,
      "preLaunchTask": "build"
    },
    {
      "name": "Worker (Debug)",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/src/SharepointDocManager.Worker/bin/Debug/net8.0/SharepointDocManager.Worker.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/SharepointDocManager.Worker",
      "stopAtEntry": false,
      "preLaunchTask": "build"
    }
  ]
}
```

Press **F5** to start debugging with breakpoints.

### 7.2 Visual Studio Debugging

1. Open solution: `SharepointDocManager.sln`
2. Set startup project: **SharepointDocManager.Api** (right-click → Set as Startup Project)
3. Press **F5** or **Debug** > **Start Debugging**
4. Set breakpoints by clicking left margin

### 7.3 Application Logging

Logs use **Serilog** with Compact JSON format (stdout).

Adjust log level in `appsettings.Development.json`:

```json
{
  "Serilog": {
    "MinimumLevel": "Debug",
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
        }
      }
    ]
  }
}
```

### 7.4 Monitor Background Services

Worker services run in background. Monitor via:

```bash
# View worker logs
docker-compose logs -f worker

# Or run worker directly in terminal (see Step 5.3)
cd src/SharepointDocManager.Worker
dotnet run --configuration Debug
```

## Step 8: Running Tests

### 8.1 Build All Tests

```bash
dotnet test --configuration Debug --verbosity normal
```

### 8.2 Run Specific Test Class

```bash
dotnet test --filter "FullyQualifiedName~DocumentServiceTests" `
  --configuration Debug --verbosity detailed
```

### 8.3 Run with Code Coverage

```bash
dotnet test /p:CollectCoverage=true `
  /p:CoverletOutputFormat=opencover `
  /p:CoverletOutput=./coverage/ `
  --configuration Debug
```

Coverage report generated in `coverage/coverage.opencover.xml`.

## Performance Testing Locally

### Load Testing with k6

1. Install k6: https://k6.io/docs/getting-started/installation/

2. Create `load-test.js`:

```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 10 },   // Ramp up
    { duration: '1m30s', target: 50 }, // Hold
    { duration: '30s', target: 0 },    // Ramp down
  ],
  thresholds: {
    'http_req_duration': ['p(95)<500', 'p(99)<1000'],
    'http_req_failed': ['rate<0.1'],
  },
};

export default function () {
  const res = http.get('https://localhost:7001/health', {
    headers: { 'kv-disable-verification': 'true' },
  });

  check(res, {
    'status is 200': (r) => r.status === 200,
    'response time < 500ms': (r) => r.timings.duration < 500,
  });

  sleep(1);
}
```

3. Run test:

```bash
k6 run load-test.js --insecure-skip-tls-verify
```

## Code Style & Standards

### .NET Code Style

Following **Microsoft C# Coding Conventions**:

```bash
# Format code
dotnet format src/SharepointDocManager.sln

# Analyze code
dotnet analyzers src/SharepointDocManager.sln
```

### Naming Conventions

| Type | Convention | Example |
|------|-----------|---------|
| Classes | PascalCase | `DocumentUploadService` |
| Methods | PascalCase | `UploadDocumentAsync` |
| Properties | PascalCase | `DocumentId` |
| Parameters | camelCase | `documentId` |
| Private fields | _camelCase | `_logger` |
| Constants | UPPER_CASE | `MAX_RETRIES` |
| Interfaces | IPascalCase | `IDocumentStorageAdapter` |

## Common Development Tasks

### Adding a New API Endpoint

1. Add command/query in `Application/Commands` or `Application/Queries`
2. Create handler in `Application/Handlers`
3. Register in `Program.cs` DI container
4. Create controller action in `Api/Controllers/*`
5. Add unit test
6. Test with Swagger UI

Example:

```csharp
// Command
namespace SharepointDocManager.Application.Commands;
public record CreateNewFolderCommand(
    string ClientId,
    string ParentFolderId,
    string FolderName
);

// Handler
public class CreateNewFolderHandler : ICommandHandler<CreateNewFolderCommand, FolderResult>
{
    // Implementation...
}

// DI Registration (Program.cs)
builder.Services.AddScoped<ICommandHandler<CreateNewFolderCommand, FolderResult>, CreateNewFolderHandler>();

// Controller
[HttpPost("folders")]
public async Task<IActionResult> CreateFolder([FromBody] CreateNewFolderCommand command)
{
    var result = await _handler.Handle(command);
    return Ok(result);
}
```

### Adding a Background Job

1. Create worker class inheriting `BackgroundService`
2. Implement `ExecuteAsync` with `CancellationToken`
3. Register in `Program.cs` with `AddHostedService<T>()`
4. Add logging/diagnostics

Example:

```csharp
public class MyCustomWorker : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<MyCustomWorker> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Custom worker running...");
                // Do work
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom worker error");
            }
        }
    }
}

// Register: builder.Services.AddHostedService<MyCustomWorker>();
```

## Troubleshooting Development Issues

### Issue: "Docker daemon not running"
```bash
# Start Docker Desktop (macOS/Windows) or
sudo systemctl start docker  # Linux
```

### Issue: "Port already in use"
```bash
# Find process using port
lsof -i :7001  # macOS/Linux
netstat -ano | findstr :7001  # Windows

# Kill process or use different port in docker-compose.yml
```

### Issue: "SQL Server connection failed"
```bash
# Check SQL Server is running
docker-compose ps mssql
docker-compose logs mssql

# Restart SQL Server
docker-compose restart mssql

# Wait 10-15 seconds for SQL to be ready
```

### Issue: "NuGet package not found"
```bash
# Clear NuGet cache
dotnet nuget locals all --clear

# Restore packages
dotnet restore --force
```

### Issue: "Certificate error in HTTPS requests"
```bash
# For localhost testing, use -SkipCertificateCheck or --insecure-skip-tls-verify
# Or run with `http` (not https) in development
```

## Next Steps After Setup

1. ✅ Explore the codebase structure (see README.md)
2. ✅ Read ARCHITECTURE.md for design patterns
3. ✅ Review existing GraphAdapter implementations
4. ✅ Write a simple test to understand the testing patterns
5. ✅ Deploy locally and test end-to-end
6. ✅ Read deployment docs for prod setup

---

**Last Updated**: April 7, 2026
**Version**: 1.0.0
