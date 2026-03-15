# Office Script Workflow POC

Enterprise-grade .NET Worker Service that drives Excel workbook operations via
Power Automate and Office Scripts — replacing SpreadsheetGear/.NET Excel libraries
with a fully cloud-hosted, modern-Excel-function-capable architecture.

---

## Why This Architecture?

| Problem with SpreadsheetGear | Solution |
|------------------------------|----------|
| Modern Excel functions not supported (FILTER, SORT, XLOOKUP, LAMBDA) | Office Scripts run inside Excel — every native function works |
| Dynamic arrays unavailable | `getSpillingToRange()` follows spill boundaries at runtime |
| Hard to maintain, proprietary API surface | TypeScript Office Scripts are versioned in Git, testable in browser |
| Requires Excel installation on server | Runs entirely on Microsoft 365 cloud infrastructure |
| Licensing cost per server | Included in M365 E3+ |

---

## Architecture

```
  ┌─────────────────────────────────────────────────────────────────────────┐
  │                        .NET Worker Service                               │
  │                                                                          │
  │  Producer code                                                           │
  │  (Timer / API / Event)                                                   │
  │       │                                                                  │
  │       ▼                                                                  │
  │  IOperationQueue ──────────────────────────────────────────────────┐    │
  │  (InMemory or Azure Service Bus)                                   │    │
  │                                                                    ▼    │
  │                                              ExcelOperationWorker        │
  │                                              (BackgroundService)         │
  │                                              Sequential — one op         │
  │                                              at a time per replica       │
  │                                              ┌──────────────────┐       │
  │                                              │  DI Scope / Op   │       │
  │                                              │  IExcelWorkbook  │       │
  │                                              │  Service         │       │
  │                                              └────────┬─────────┘       │
  │                                                       │                  │
  │                                              IWorkbookRegistry           │
  │                                              (resolves flow URLs         │
  │                                               by WorkbookId)            │
  │                                                       │                  │
  │                                              IPowerAutomateClient        │
  │                                              (typed HttpClient)          │
  │                                                       │                  │
  │       HTTP Pipeline (outermost → innermost):          │                  │
  │       CircuitBreaker → Retry → AsyncPollingHandler    │                  │
  │       → CorrelationIdHandler → HttpClient             │                  │
  └───────────────────────────────────────────────────────┼─────────────────┘
                                                          │
                          HTTPS (SAS-signed URL)          │
                                                          ▼
  ┌───────────────────────────────────────────────────────────────────────────┐
  │                     Power Automate (Premium)                               │
  │                                                                            │
  │  ┌────────────────┐   ┌────────────────┐   ┌────────────────┐            │
  │  │ InsertRange    │   │ UpdateRange    │   │ ExtractRange   │            │
  │  │ Flow           │   │ Flow           │   │ Flow           │            │
  │  │                │   │                │   │                │            │
  │  │ HTTP Trigger   │   │ HTTP Trigger   │   │ HTTP Trigger   │            │
  │  │ (SAS-signed)   │   │ (SAS-signed)   │   │ (SAS-signed)   │            │
  │  │       ↓        │   │       ↓        │   │ Condition:     │            │
  │  │  Run Script    │   │  Run Script    │   │  AnchorCell?   │            │
  │  │       ↓        │   │       ↓        │   │   ↓       ↓   │            │
  │  │  Response      │   │  Response      │   │ Dynamic Static │            │
  │  └────────────────┘   └────────────────┘   └────────────────┘            │
  │                                                                            │
  │  Long-running flows (>2min) automatically return 202 Accepted              │
  │  + Location header → AsyncPollingHandler polls until 200 OK               │
  └─────────────────────────────────────────────────────┬─────────────────────┘
                                                        │
                    Excel Online API                    │
                                                        ▼
  ┌─────────────────────────────────────────────────────────────────────────────┐
  │               SharePoint Document Library                                    │
  │                                                                              │
  │  ┌──────────────────────────────────────────────────────┐                  │
  │  │  Excel Workbook (.xlsx)                               │                  │
  │  │                                                       │                  │
  │  │  Embedded Office Scripts:                             │                  │
  │  │  ● InsertRangeScript      ← table.addRows()          │                  │
  │  │  ● UpdateRangeScript      ← range.setValues()        │                  │
  │  │  ● ExtractRangeScript     ← range.getValues()        │                  │
  │  │  ● ExtractDynamicArray    ← getSpillingToRange()     │                  │
  │  │                                                       │                  │
  │  │  Hundreds of tables across multiple sheets            │                  │
  │  │  Dynamic array formulas: FILTER, SORT, XLOOKUP...    │                  │
  │  └──────────────────────────────────────────────────────┘                  │
  └─────────────────────────────────────────────────────────────────────────────┘
```

---

## Project Structure

```
OfficeScriptWorkflowPOC/
├── src/OfficeScriptWorkflow.Worker/
│   ├── Program.cs                              Host bootstrap + Serilog
│   ├── appsettings.json                        Configuration (no secrets)
│   │
│   ├── Configuration/
│   │   ├── WorkbookInstanceConfig.cs           Per-workbook: SiteUrl, WorkbookPath, 4 flow URLs
│   │   ├── WorkbookRegistryOptions.cs          List of all workbooks
│   │   ├── ConcurrencyOptions.cs               Parallelism, polling, HTTP timeout
│   │   ├── ResilienceConfiguration.cs          Retry count, circuit breaker thresholds
│   │   ├── ServiceBusConfiguration.cs          Queue name, session settings
│   │   └── FlowAccountPoolOptions.cs           Pool of service accounts for quota distribution
│   │
│   ├── Clients/
│   │   ├── IPowerAutomateClient.cs             URL-explicit HTTP interface
│   │   └── PowerAutomateClient.cs              Typed HttpClient, SAS masking in logs
│   │
│   ├── Services/
│   │   ├── IWorkbookRegistry.cs / WorkbookRegistry.cs     Resolves WorkbookId → config
│   │   ├── IFlowAccountPool.cs / FlowAccountPool.cs       Quota-aware round-robin account pool
│   │   ├── IExcelWorkbookService.cs / ExcelWorkbookService.cs  Domain operations + batching
│   │   ├── IOperationQueue.cs                  Operation definitions + queue interface
│   │   ├── InMemoryOperationQueue.cs           Single-replica (Channels)
│   │   ├── AzureServiceBusOperationQueue.cs    Multi-replica (session-enabled SB)
│   │   ├── IOperationResultStore.cs            Async result handoff for extract ops
│   │   └── InMemoryOperationResultStore.cs     In-process TCS store
│   │
│   ├── Workers/
│   │   └── ExcelOperationWorker.cs             BackgroundService, SemaphoreSlim concurrency
│   │
│   └── Infrastructure/
│       ├── Http/
│       │   └── AsyncPollingHandler.cs          202 → poll Location → 200 pattern
│       ├── Resilience/
│       │   ├── ResiliencePolicies.cs           Retry (exp+jitter, Retry-After) + circuit breaker
│       │   └── PowerAutomateRetryHandler.cs    Correlation ID header
│       └── DependencyInjection/
│           └── ServiceCollectionExtensions.cs  All wiring in one place
│
├── office-scripts/                             TypeScript — deployed via Deploy-OfficeScripts.ps1
│   ├── InsertRangeScript.ts                    table.addRows() with validation
│   ├── UpdateRangeScript.ts                    range.setValues() with dimension check
│   ├── ExtractRangeScript.ts                   Static range extraction
│   ├── ExtractDynamicArrayScript.ts            getSpillingToRange() for dynamic arrays
│   └── BatchOperationScript.ts                 Multi-op: insert+update+extract in one call
│
├── scripts/
│   └── Deploy-OfficeScripts.ps1                PowerShell 7+ — deploys Office Scripts via Graph API
│
├── power-automate/flow-definitions/            Reference JSON for building flows manually
│   ├── insert-range-flow.json
│   └── extract-range-flow.json
│
├── docs/
│   ├── azure-ad-setup.md                       Auth setup, SAS key management
│   ├── flow-setup.md                           Power Automate step-by-step guide
│   ├── office-scripts-deployment.md            Automated + manual script deployment
│   └── multi-replica-deployment.md             ACA/AKS/Windows Service, KEDA scaling
│
├── Dockerfile
├── docker-compose.yml
└── README.md
```

---

## Authentication Flow

```
.NET Worker
    │
    │  HTTPS POST (SAS-signed URL)
    │  No OAuth token required —
    │  SAS key in URL IS the credential
    │
    ▼
Power Automate HTTP Trigger
    │
    │  Runs under service account connection
    │  (M365 account with SharePoint Contribute)
    │
    ▼
Excel Online Business connector
    │
    ▼
SharePoint → Workbook → Office Script
```

**No Azure AD app registration is required** for the current architecture. The SAS-signed trigger
URLs are the credentials. Store them as secrets in Azure Key Vault.

---

## Configuration Reference

### Adding a workbook — SharePoint

```json
{
  "Id": "financials",
  "DisplayName": "Financials Workbook",
  "StorageType": "SharePoint",
  "SiteUrl": "https://TENANT.sharepoint.com/sites/Finance",
  "WorkbookPath": "/Shared Documents/Workbooks/Financials.xlsx",
  "BatchSize": 500,
  "InsertRangeFlowUrl": "SECRET — set via Key Vault or user-secrets",
  "UpdateRangeFlowUrl": "SECRET",
  "ExtractRangeFlowUrl": "SECRET",
  "BatchOperationFlowUrl": "SECRET — required only when using ExecuteBatchAsync()"
}
```

`StorageType` defaults to `"SharePoint"` and can be omitted. Each workbook can be on a **different SharePoint site** — the `SiteUrl` and `WorkbookPath` uniquely identify the file.

### Adding a workbook — SharePoint Embedded (SPE)

```json
{
  "Id": "risk-model",
  "DisplayName": "Risk Model (SPE Container)",
  "StorageType": "SharePointEmbedded",
  "ContainerId": "b!xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "ContainerTypeId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "SiteUrl": "https://TENANT.sharepoint.com/contentstorage/CSP_CONTAINERID",
  "WorkbookPath": "/Documents/RiskModel.xlsx",
  "BatchSize": 500,
  "InsertRangeFlowUrl": "SECRET",
  "UpdateRangeFlowUrl": "SECRET",
  "ExtractRangeFlowUrl": "SECRET",
  "BatchOperationFlowUrl": "SECRET"
}
```

`ContainerId` is required for SPE workbooks and is validated at startup. Retrieve it via:
```bash
# Graph API — list all SPE containers for your container type
GET https://graph.microsoft.com/v1.0/storage/fileStorage/containers?$filter=containerTypeId eq 'YOUR_CONTAINER_TYPE_ID'
```

The service account must be added as a container member before flows can access the workbook — see `docs/azure-ad-setup.md` for SPE permission setup.

### Enabling the service account pool (high volume)

When batch calls exceed ~13,333/day (the point where a single Premium account's 40,000
daily action limit is reached at 3 actions per call), add accounts to `FlowAccountPool`:

```json
"FlowAccountPool": {
  "Accounts": [
    {
      "AccountId": "svc-os-01",
      "DailyActionLimit": 40000,
      "InsertRangeFlowUrl":     "SECRET",
      "UpdateRangeFlowUrl":     "SECRET",
      "ExtractRangeFlowUrl":    "SECRET",
      "BatchOperationFlowUrl":  "SECRET"
    }
  ]
}
```

Each account holds its own copy of the flows (Power Automate flows are hardcoded to the
connection of the account that created them). The pool distributes calls round-robin and
automatically marks an account exhausted (until midnight UTC) on a 429 response.

See `docs/high-volume-scaling.md` for the full capacity maths and decision guide.

### Local development secrets

```bash
cd src/OfficeScriptWorkflow.Worker
dotnet user-secrets init
dotnet user-secrets set "WorkbookRegistry:Workbooks:0:InsertRangeFlowUrl" "https://prod-XX...&sig=..."
dotnet user-secrets set "WorkbookRegistry:Workbooks:0:UpdateRangeFlowUrl" "https://prod-XX...&sig=..."
dotnet user-secrets set "WorkbookRegistry:Workbooks:0:ExtractRangeFlowUrl" "https://prod-XX...&sig=..."
```

### Enqueuing operations from your code

```csharp
// Single replica (in-memory queue)
var queue = serviceProvider.GetRequiredService<IOperationQueue>();

// Insert rows
await queue.EnqueueAsync(new InsertRowsOperation("Sheet1", "SalesTable", rowData)
{
    WorkbookId = "workbook-01"
});

// Update a range
await queue.EnqueueAsync(new UpdateRangeOperation("Sheet1", "B2:D10", values)
{
    WorkbookId = "workbook-01"
});

// Extract dynamic array (with result awaiting)
var op = new ExtractDynamicArrayOperation("Summary", "A2")
{
    WorkbookId = "workbook-01"
};
await queue.EnqueueAsync(op);

var resultStore = serviceProvider.GetRequiredService<IOperationResultStore>();
var data = await resultStore.WaitForResultAsync(op.Id, timeout: TimeSpan.FromMinutes(5), ct);
```

---

## Key Design Decisions

### 1. Async polling for long-running scripts (>2 min)

Power Automate / Logic Apps automatically switches to the async HTTP pattern when
flow execution exceeds ~2 minutes:
- Returns `202 Accepted` with a `Location` polling URL and `Retry-After` header
- `AsyncPollingHandler` follows this transparently — callers see only the final `200 OK`
- Total wall-clock budget controlled by `Concurrency:MaxPollingDurationMinutes`

### 2. Batching for Office Script timeout

Office Scripts have a hard 5-minute execution timeout. `ExcelWorkbookService` chunks
rows via `.Chunk(BatchSize)` and calls the insert flow in serial batches, each well
within the timeout.

### 3. Multi-workbook routing via registry

All operations carry a `WorkbookId`. `WorkbookRegistry` resolves it to the correct
set of flow URLs (each workbook has dedicated flows hardcoded to its SharePoint file).
`PowerAutomateClient` is URL-agnostic — it receives the URL per call, not from config.

### 4. One workbook per replica — scale by adding replicas

Each worker replica processes operations for **one workbook at a time**, sequentially.
There is no in-replica parallelism. Parallel throughput across multiple workbooks is
achieved by running more replicas — each replica acquires a different workbook session
from Service Bus (`SessionId = WorkbookId`). Service Bus guarantees exactly one active
session receiver per session, so two replicas can never write to the same workbook
simultaneously.

```
Replica 1 → acquires session for workbook-01 → processes all ops → picks up next session
Replica 2 → acquires session for workbook-02 → processes all ops → picks up next session
Replica 3 → acquires session for workbook-03 → processes all ops → picks up next session
```

KEDA scales replica count based on the number of active sessions in the Service Bus queue.

### 5. High-volume batching via BatchOperationScript

`IExcelWorkbookService.ExecuteBatchAsync()` sends 40–50 operations in a single Power Automate
call instead of making one call per operation. `BatchOperationScript.ts` processes the entire
list inside one Office Script execution (up to 5 minutes), returning all results in one response.

This reduces 500,000 individual operations/day to ~12,500 batch calls/day — staying within
a single Premium account's 40,000 daily action limit and avoiding the need for a large
account pool. See `docs/high-volume-scaling.md`.

### 6. Quota-aware service account pool via IFlowAccountPool

`FlowAccountPool` distributes calls round-robin across multiple M365 service accounts when
batch volume still exceeds a single account's quota. On a 429 response, the account is
marked exhausted until midnight UTC and calls route to the next available account automatically.
Each account in the pool owns its own set of flows (same logic, different SAS URLs).

### 7. Pluggable queue via IOperationQueue

`ServiceBus:UseServiceBus = false` → in-memory `Channel` (single-replica, zero infra).
`ServiceBus:UseServiceBus = true` → Azure Service Bus (multi-replica, KEDA autoscaling).
No application code changes — only configuration.

---

## Resilience Stack (per HTTP call)

```
Circuit Breaker (5 failures → 30s break)
    └─ Retry (4 attempts, exponential + jitter, respects Retry-After on 429)
        └─ AsyncPollingHandler (follows 202 Location until 200, up to 10 min)
            └─ PowerAutomateRetryHandler (adds x-correlation-id header)
                └─ HttpClient (120s per-attempt timeout)
```

---

## Getting Started

### Pre-requisites

- .NET 10 SDK
- M365 tenant with Power Automate Premium plan
- SharePoint site with Contribute access
- Excel workbook uploaded to SharePoint
- Office Scripts enabled in the tenant (M365 admin centre → Settings → Org settings → Office Scripts)

### 1. Clone and build

```bash
git clone <repo>
cd OfficeScriptWorkflowPOC
dotnet build
```

### 2. Deploy Office Scripts

Scripts are deployed **once per service account** to the account's OneDrive ("My Scripts"),
not once per workbook. Every Power Automate flow that runs under the service account can
then reference the deployed scripts regardless of which workbook it targets.

Requires the `OfficeScriptWorkflowPOC-ScriptDeployer` app registration
(`Files.ReadWrite.All`, admin consented) — see `docs/azure-ad-setup.md`.

```powershell
cd <repo-root>
pwsh scripts/Deploy-OfficeScripts.ps1 `
  -TenantId    YOUR_TENANT_ID `
  -ClientId    YOUR_DEPLOYER_APP_ID `
  -ClientSecret (Read-Host -AsSecureString) `
  -ServiceAccountUpns "svc-officescript@contoso.onmicrosoft.com"
```

Provide every service account UPN that owns Power Automate flows (primary account plus any
`FlowAccountPool` accounts). Add `-WhatIf` to preview actions without making any changes.
For manual deployment (no app registration), see `docs/office-scripts-deployment.md`.

### 3. Create Power Automate flows

Follow `docs/flow-setup.md` to create the 3 flows per workbook and copy their SAS URLs.

### 4. Configure secrets

```bash
cd src/OfficeScriptWorkflow.Worker
dotnet user-secrets set "WorkbookRegistry:Workbooks:0:InsertRangeFlowUrl" "..."
# (repeat for Update and Extract URLs)
```

Update `appsettings.json` with SharePoint SiteUrl and WorkbookPath.

### 5. Run

```bash
dotnet run --project src/OfficeScriptWorkflow.Worker
```

### 6. Deploy to production

See `docs/multi-replica-deployment.md` for:
- Docker / Azure Container Apps (recommended)
- Azure Kubernetes Service with KEDA autoscaling
- Windows Service (on-premises)

---

## Operational Notes

| Topic | Detail |
|-------|--------|
| **Flow URL rotation** | SAS keys in trigger URLs do not expire by default. If rotated, update Key Vault and restart the worker. |
| **Service account** | Create a dedicated M365 account for Power Automate connections. Do NOT use personal accounts — connections break when staff leave. |
| **Office Script updates** | Edit `.ts` files and merge to main. CI pipeline runs `Deploy-OfficeScripts.ps1` — change is live immediately. |
| **Power Automate throttling** | Premium: 40,000 actions/day per account (3 actions per flow run). At 500K ops/day, use `ExecuteBatchAsync()` to reduce to ~12,500 calls. See `docs/high-volume-scaling.md`. |
| **Account pool quota** | `FlowAccountPool` marks an account exhausted on HTTP 429 until midnight UTC and routes to the next. Monitor daily consumption in Power Automate admin centre. |
| **Workbook concurrency** | Office Scripts lock the workbook during execution. Service Bus sessions enforce single-writer. |
| **Large workbooks** | For workbooks >100 MB, consider splitting into multiple focused workbooks. |
