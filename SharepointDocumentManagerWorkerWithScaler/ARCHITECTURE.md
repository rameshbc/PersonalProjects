# Enterprise SharePoint Document Manager — Architecture Plan

## Table of Contents

1. [Overview](#1-overview)
2. [Requirements Summary](#2-requirements-summary)
3. [Solution Structure](#3-solution-structure)
4. [Authentication & Identity](#4-authentication--identity)
5. [SP vs SPE Abstraction](#5-sp-vs-spe-abstraction-adapter-pattern)
6. [Graph API Patterns](#6-graph-api-patterns)
7. [Permission Model](#7-permission-model)
8. [Parallel Processing Architecture](#8-parallel-processing-architecture)
9. [Excel Workbook Integration](#9-excel-workbook-integration)
10. [Admin Portal Features](#10-admin-portal-features)
11. [Migration Scripts](#11-migration-scripts-powershell-one-time)
12. [Data Store](#12-data-store-application-db)
13. [Key NuGet Packages](#13-key-nuget-packages)
14. [Implementation Phases](#14-implementation-phases)
15. [File & Folder Conventions](#15-file--folder-conventions)

---

## 1. Overview

A multi-tenant enterprise document management system that manages client documents in
their respective SharePoint Online (SP) sites, targeting a single document library
`DocLibrary-A` per client. The system is migrating from CSOM + Basic Auth (deprecated)
to **Microsoft Graph API + Managed Identity**, and must support both SharePoint Online
and **SharePoint Embedded (SPE)** simultaneously — 50% of clients on each platform.

### Typical Client Site Structure

```
DocLibrary-A (Document Library)
└── RootParent
    ├── Parent-A
    │   └── Child-A
    │       ├── ExcelDocument-1.xlsx
    │       └── ExcelDocument-2.xlsx
    ├── Parent-B
    │   └── Child-A
    │       ├── ExcelDocument-1.xlsx
    │       └── ExcelDocument-2.xlsx
    └── Parent-C
        ├── Child-A
        │   ├── ExcelDocument-1.xlsx
        │   └── ExcelDocument-2.xlsx
        └── Child-B  ← Protected folder (Admin only)
            └── ExcelDocument-1.xlsx
```

---

## 2. Requirements Summary

| Concern | Detail |
|---|---|
| **Auth** | Managed Identity → Graph API. No user credentials in the server-to-server flow. |
| **Storage** | SharePoint Online (SP) + SharePoint Embedded (SPE). Dual-mode, per-client config. |
| **Scale** | Thousands of clients. Parallel ops. Throttling-resilient. |
| **Roles** | Admin / Contributor / Read — enforced at subfolder (Parent) level. |
| **UI** | Document list, version history, online edit launch via browser. |
| **Excel** | Server-side workbook read/write via Graph `/workbook` endpoints. No Excel installed. |
| **Admin** | Site/library provisioning, permission management for SP and SPE. |
| **Migration** | (A) Grant MI permissions to existing SP sites. (B) Move content SP → SPE per client. |

---

## 3. Solution Structure

```
SharepointDocumentManager/
│
├── src/
│   ├── SharepointDocManager.Core/              # Domain layer — zero framework dependencies
│   │   ├── Entities/
│   │   │   ├── ClientSite.cs
│   │   │   ├── DocumentFolder.cs
│   │   │   ├── DocumentItem.cs
│   │   │   ├── DocumentVersion.cs
│   │   │   └── PermissionGroup.cs
│   │   ├── Enums/
│   │   │   ├── StorageBackend.cs               # SharePointOnline | SharePointEmbedded
│   │   │   ├── DocumentRole.cs                 # Admin | Contributor | Reader
│   │   │   └── FolderLevel.cs                  # Root | Parent | Child
│   │   ├── Interfaces/
│   │   │   ├── IDocumentStorageAdapter.cs      # Single contract for SP + SPE
│   │   │   ├── IFolderService.cs
│   │   │   ├── IDocumentService.cs
│   │   │   ├── IPermissionService.cs
│   │   │   ├── IExcelWorkbookService.cs
│   │   │   ├── IVersionHistoryService.cs
│   │   │   └── IClientSiteRepository.cs
│   │   └── Models/
│   │       ├── UploadRequest.cs
│   │       ├── FolderStructureSpec.cs
│   │       └── BatchOperationResult.cs
│   │
│   ├── SharepointDocManager.Infrastructure/    # Framework & external service implementations
│   │   ├── Graph/
│   │   │   ├── GraphClientFactory.cs           # DefaultAzureCredential / ManagedIdentity
│   │   │   ├── GraphBatchExecutor.cs           # $batch — up to 20 requests per call
│   │   │   ├── GraphThrottlingHandler.cs       # DelegatingHandler — 429 + Retry-After
│   │   │   └── GraphUploadSessionManager.cs    # Resumable upload for files > 4 MB
│   │   ├── Adapters/
│   │   │   ├── SharePointAdapter.cs            # IDocumentStorageAdapter for SP Online
│   │   │   └── SharePointEmbeddedAdapter.cs    # IDocumentStorageAdapter for SPE
│   │   ├── Resilience/
│   │   │   ├── ResiliencePipelineRegistry.cs   # Polly v8 pipelines per client tier
│   │   │   └── BulkheadPolicy.cs               # Per-client concurrency isolation
│   │   ├── Permissions/
│   │   │   ├── SharePointPermissionService.cs
│   │   │   └── SpePermissionService.cs
│   │   └── Excel/
│   │       └── ExcelWorkbookService.cs         # Graph /workbook endpoints
│   │
│   ├── SharepointDocManager.Application/       # Business logic — no UI, no framework deps
│   │   ├── Commands/                           # Command objects (no MediatR)
│   │   │   ├── CreateFolderStructureCommand.cs
│   │   │   ├── UploadDocumentCommand.cs
│   │   │   ├── BatchUploadDocumentsCommand.cs
│   │   │   ├── GrantFolderPermissionsCommand.cs
│   │   │   └── ProvisionClientSiteCommand.cs
│   │   ├── Queries/
│   │   │   ├── GetDocumentListQuery.cs
│   │   │   ├── GetVersionHistoryQuery.cs
│   │   │   └── GetOnlineEditUrlQuery.cs
│   │   ├── Services/
│   │   │   ├── DocumentOrchestrationService.cs # Coordinates parallel batch uploads
│   │   │   ├── FolderProvisioningService.cs    # Creates folder tree + breaks inheritance
│   │   │   └── StorageAdapterResolver.cs       # Resolves SP or SPE adapter per client
│   │   └── Handlers/
│   │       ├── CreateFolderStructureHandler.cs
│   │       ├── UploadDocumentHandler.cs
│   │       ├── BatchUploadDocumentsHandler.cs
│   │       ├── GrantFolderPermissionsHandler.cs
│   │       ├── GetDocumentListHandler.cs
│   │       ├── GetVersionHistoryHandler.cs
│   │       └── GetOnlineEditUrlHandler.cs
│   │
│   ├── SharepointDocManager.Api/               # ASP.NET Core Web API
│   │   ├── Controllers/
│   │   │   ├── DocumentsController.cs
│   │   │   ├── FoldersController.cs
│   │   │   ├── VersionHistoryController.cs
│   │   │   └── AdminController.cs
│   │   ├── Hubs/
│   │   │   └── UploadProgressHub.cs            # SignalR — real-time upload progress
│   │   ├── Middleware/
│   │   │   └── ClientContextMiddleware.cs      # Resolves ClientId from JWT / header
│   │   └── Program.cs
│   │
│   ├── SharepointDocManager.Worker/            # .NET Worker Service — background processing
│   │   ├── Workers/
│   │   │   ├── BatchUploadWorker.cs            # Channel-based producer/consumer
│   │   │   └── PermissionSyncWorker.cs         # Delta query-based permission drift check
│   │   └── Program.cs
│   │
│   └── SharepointDocManager.Admin/             # Blazor Server admin portal
│       ├── Pages/
│       │   ├── SiteProvisioning.razor
│       │   ├── LibraryPermissions.razor
│       │   ├── ClientList.razor
│       │   └── StorageBackendToggle.razor      # Switch client SP ↔ SPE
│       └── Services/
│           └── AdminApiClient.cs
│
├── tests/
│   ├── Unit/
│   │   ├── SharepointDocManager.Core.Tests/
│   │   ├── SharepointDocManager.Application.Tests/
│   │   └── SharepointDocManager.Infrastructure.Tests/
│   ├── Integration/
│   │   └── SharepointDocManager.Integration.Tests/
│   └── LoadTests/
│       └── SharepointDocManager.LoadTests/     # k6 or NBomber
│
└── Migration/                                  # PowerShell — one-time ops only
    ├── README.md
    ├── Shared/
    │   ├── Auth-Helpers.ps1                    # Token acquisition (MI / ClientCreds / Interactive)
    │   └── Graph-Helpers.ps1                   # Throttle-aware Graph REST wrapper + paging
    ├── ScriptA-GrantMIPermissions/
    │   ├── Grant-ManagedIdentityPermissions.ps1
    │   └── clients-template.csv
    └── ScriptB-MigrateSpToSpe/
        ├── Migrate-SpToSpe.ps1
        └── migration-config-template.json
```

---

## 4. Authentication & Identity

```
App Service / AKS Pod
       │
       ▼  (no secrets in code or config)
User-Assigned Managed Identity
       │
       ▼
Microsoft Entra ID  →  Token  (scope: https://graph.microsoft.com/.default)
       │
       ├──►  SharePoint Online sites       (Sites.Selected per site)
       └──►  SharePoint Embedded containers (FileStorageContainer.Selected)
```

### Key Decisions

| Decision | Rationale |
|---|---|
| **User-Assigned MI** (not system-assigned) | Portable across multiple App Services, AKS pods. Can be pre-created and assigned before deployment. |
| **Sites.Selected** (not Sites.ReadWrite.All) | Limits MI access to only explicitly listed client sites. Massive blast-radius reduction. |
| **No delegated permissions** | Users never authenticate to the server. All SP/SPE access is app-only. |
| **`DefaultAzureCredential`** in code | Automatically uses MI in prod; falls back to `AzureCliCredential` in dev — no code change needed. |

### Required Graph Application Permissions

| Permission | Purpose |
|---|---|
| `Sites.Selected` | Scoped SP site read/write for the MI |
| `Files.ReadWrite.All` | Drive item operations (upload, download, folder create) |
| `Group.ReadWrite.All` | Entra group management for role groups |
| `FileStorageContainer.Selected` | SPE container access (scoped per container) |

---

## 5. SP vs SPE Abstraction (Adapter Pattern)

The **Adapter pattern** isolates all SP/SPE differences behind a single interface.
All application services call `IDocumentStorageAdapter` — they never branch on storage backend.

```csharp
// SharepointDocManager.Core/Interfaces/IDocumentStorageAdapter.cs
public interface IDocumentStorageAdapter
{
    Task<string>                    CreateFolderAsync(CreateFolderRequest req, CancellationToken ct);
    Task<DocumentItem>              UploadDocumentAsync(UploadRequest req, CancellationToken ct);
    Task<IReadOnlyList<DocumentItem>>   ListDocumentsAsync(string folderId, CancellationToken ct);
    Task<IReadOnlyList<DocumentVersion>> GetVersionHistoryAsync(string itemId, CancellationToken ct);
    Task<string>                    GetOnlineEditUrlAsync(string itemId, CancellationToken ct);
    Task                            SetFolderPermissionsAsync(PermissionRequest req, CancellationToken ct);
    Task<BatchOperationResult>      BatchUploadAsync(IEnumerable<UploadRequest> reqs, CancellationToken ct);
}
```

**`StorageAdapterResolver`** (in Application layer) looks up `ClientConfig.StorageBackend`
and returns the correct adapter. This is the **only branch** in the entire codebase — all
callers above it are adapter-agnostic.

---

## 6. Graph API Patterns

### 6a. Folder Creation — Idempotent

```
POST /sites/{siteId}/drives/{driveId}/items/{parentId}/children
Body: { "name": "...", "folder": {}, "@microsoft.graph.conflictBehavior": "fail" }

→ 201 Created  : new folder, capture ID
→ 409 Conflict : already exists — treat as success, fetch existing item ID
```

The `FolderProvisioningService` walks the `FolderStructureSpec` depth-first so
parent folders always exist before children.

### 6b. Document Upload Strategy

| File Size | Strategy | Graph Endpoint |
|---|---|---|
| < 4 MB | Single PUT | `PUT /drives/{id}/items/{parentId}:/{name}:/content` |
| 4 MB – 250 MB | Resumable upload session | `POST .../createUploadSession` → chunk PUTs |
| > 250 MB | Resumable session + retry per chunk | Same — 5 MB chunks (multiple of 320 KB) |

### 6c. Graph `$batch` for Parallel Operations

- Up to **20 requests** per batch payload (`POST /$batch`)
- `GraphBatchExecutor` partitions work into batches of 20
- Handles **partial failures**: only failed sub-requests are retried on next batch cycle
- Used for: bulk folder creates, bulk metadata updates, bulk permission checks

### 6d. Throttling Handler

```
GraphThrottlingHandler  (DelegatingHandler in the HttpClient pipeline)
  ├── On HTTP 429 → read Retry-After header → wait exactly that duration + jitter
  ├── On HTTP 503 → exponential back-off (2^attempt × 5 s, capped at 120 s)
  └── After MaxRetries exhausted → throw, let circuit breaker decide
```

Polly v8 `ResiliencePipeline` is registered per **client tier** (Gold / Standard)
allowing different concurrency limits and retry budgets per tier.

### 6e. Delta Queries for Change Tracking

```
GET /drives/{driveId}/root/delta?token={deltaToken}
```

Used by `PermissionSyncWorker` to detect permission drift since last sync —
avoids full folder scans at scale. Delta token stored per client in the DB.

---

## 7. Permission Model

```
DocLibrary-A
└── RootParent  (inherits from site — no custom grants)
    ├── Parent-A  ← break inheritance → grant Entra groups:
    │              {ClientId}-Admin        → Owner
    │              {ClientId}-Contributor  → Write
    │              {ClientId}-Reader       → Read
    ├── Parent-B  ← same pattern
    └── Parent-C
        ├── Child-A  ← inherits from Parent-C (no break)
        └── Child-B  ← break inheritance → {ClientId}-Admin only (protected)
```

### Group Naming Convention

```
{ClientId}-Admin         e.g. client-001-Admin
{ClientId}-Contributor   e.g. client-001-Contributor
{ClientId}-Reader        e.g. client-001-Reader
```

Groups are Entra ID Security Groups. Membership is managed externally (HR system /
IdP sync) — the application only manages **folder-level assignment**, not membership.

### Permission Service Flow

1. Create folder (idempotent)
2. `DELETE /drives/{driveId}/items/{folderId}/permissions` — remove inherited permissions (break inheritance)
3. `POST /drives/{driveId}/items/{folderId}/invite` — grant each group its role
4. For Child-B (protected): skip step 3 Contributor/Reader grants

SPE folders follow the same logic but use container-level permission scopes.

---

## 8. Parallel Processing Architecture

```
API Request / Admin Trigger
        │
        │  BatchUploadDocumentsCommand
        ▼
DocumentOrchestrationService
        │
        ▼
Channel<UploadRequest>  ← bounded capacity (backpressure prevents OOM)
        │
        ├──► Upload Worker 1 ──► GraphBatchExecutor (20-req batch)
        ├──► Upload Worker 2 ──►      │
        └──► Upload Worker 3 ──►      │
                                      ▼
                          Per-client SemaphoreSlim  ← bulkhead isolation
                                      │
                                      ▼
                             Polly RetryPipeline  ← 429 handled here
                                      │
                                      ▼
                              Microsoft Graph API
```

- `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` from config (per tier)
- `Channel<T>` for producer/consumer decoupling — bounded channel provides natural backpressure
- `SemaphoreSlim` per client prevents one busy client starving others (bulkhead)
- **SignalR** `UploadProgressHub` pushes real-time upload progress events to the UI

---

## 9. Excel Workbook Integration

All Excel operations use Graph `/workbook` endpoints — no Excel installed server-side,
no COM interop, no NPOI/EPPlus for the SP/SPE-hosted files.

| Operation | Graph Endpoint |
|---|---|
| Read range | `GET /drives/{id}/items/{id}/workbook/worksheets/{name}/usedRange` |
| Write range | `PATCH /drives/{id}/items/{id}/workbook/worksheets/{name}/range(address='A1:D10')` |
| Create session | `POST /drives/{id}/items/{id}/workbook/createSession` |
| Close session | `POST /drives/{id}/items/{id}/workbook/closeSession` |
| List sheets | `GET /drives/{id}/items/{id}/workbook/worksheets` |

**Session pattern for multi-step edits:**
1. `createSession` with `persistChanges: true` → receive `workbookSessionId`
2. Include `workbook-session-id` header on all subsequent calls
3. `closeSession` when done — commits changes
4. For read-only access: `persistChanges: false` — no session close needed

---

## 10. Admin Portal Features

| Feature | Implementation |
|---|---|
| Provision new SP site + DocLibrary-A | Graph `POST /sites`, create drive, run `FolderProvisioningService` |
| Create SPE container for client | Graph `POST /storage/fileStorage/containers` with containerTypeId |
| Grant MI `Sites.Selected` to existing site | Admin calls backend which calls `POST /sites/{id}/permissions` |
| Toggle client storage: SP ↔ SPE | Update `ClientConfig.StorageBackend`; `StorageAdapterResolver` picks it up immediately |
| View library folder tree | Drive delta listing, rendered as interactive tree component |
| Manage Entra role groups | Graph Group CRUD — create, add/remove members, list |
| View audit log | Structured log entries from Azure Monitor / Application Insights |
| Client migration status | Table view of per-client migration progress (for SP → SPE moves) |

All admin actions are logged to the `AuditLog` table with actor, action, resource, and timestamp.

---

## 11. Migration Scripts (PowerShell — One-Time)

Both scripts are located in `Migration/`. They are **not** part of the .NET solution.
They are one-time operator-run scripts. Both are idempotent — safe to re-run.

### Script A — Grant MI Permissions to Existing SP Sites

**File:** `Migration/ScriptA-GrantMIPermissions/Grant-ManagedIdentityPermissions.ps1`

**Purpose:** Grants the application's Managed Identity the `write` role on each
existing client's SharePoint site using the Graph `Sites.Selected` approach.

**Flow:**
```
For each client site in clients.csv:
  1. Resolve site ID from URL  (cached in CSV after first run)
  2. Check if MI permission already exists  → skip if yes (idempotent)
  3. POST /sites/{siteId}/permissions
     { roles: ["write"], grantedToIdentities: [{ application: { id: <MI-AppId> } }] }
  4. Log result to JSON-Lines audit file
```

**Auth required:** SharePoint Admin or Global Admin (Interactive or bootstrap ClientCredentials).

### Script B — Migrate Content SP → SPE

**File:** `Migration/ScriptB-MigrateSpToSpe/Migrate-SpToSpe.ps1`

**Purpose:** For each client switching to SPE, copies the full `DocLibrary-A` content
(folder structure + files) from SP to the pre-created SPE container.

**Flow:**
```
For each client in migration-config.json:
  1. Resolve SP site ID + drive ID
  2. Recursively enumerate DocLibrary-A (folders depth-first, then files)
  3. For each folder: POST /drives/{speContainerId}/items/{parentId}/children  (idempotent)
  4. For each file:
       < 4 MB  → single PUT to SPE drive item content endpoint
       ≥ 4 MB  → createUploadSession + chunked PUT (5 MB chunks)
  5. If PreserveFolderPermissions = true → replicate SP group grants on SPE folders
  6. Track migrated item IDs in migration-state.json  (resumable)
  7. After client sign-off → flip StorageBackend flag in ClientConfig DB record
```

**SP content is never deleted** — the SP library remains intact until the client
explicitly confirms the migration and the Admin portal flips the backend toggle.

---

## 12. Data Store (Application DB)

The application database stores client configuration and operational data.
Managed via EF Core with a SQL Azure backend.

### Core Tables

#### `ClientConfig`

| Column | Type | Description |
|---|---|---|
| `ClientId` | `nvarchar(100)` PK | Stable client identifier |
| `TenantId` | `uniqueidentifier` | Client's Entra tenant |
| `StorageBackend` | `nvarchar(20)` | `SharePointOnline` or `SharePointEmbedded` |
| `SpSiteId` | `nvarchar(200)` | Graph site ID (SP) |
| `SpDriveId` | `nvarchar(200)` | DocLibrary-A drive ID (SP) |
| `SpeContainerId` | `nvarchar(200)` | SPE container drive ID |
| `DeltaToken` | `nvarchar(max)` | Last delta token for permission sync |
| `CreatedAt` | `datetimeoffset` | |
| `UpdatedAt` | `datetimeoffset` | |

#### `AuditLog`

| Column | Type | Description |
|---|---|---|
| `Id` | `bigint` PK | |
| `ActorId` | `nvarchar(200)` | User or system principal |
| `Action` | `nvarchar(100)` | e.g. `FolderCreated`, `PermissionGranted` |
| `ResourceId` | `nvarchar(500)` | Drive item ID, site ID, etc. |
| `ClientId` | `nvarchar(100)` | |
| `Timestamp` | `datetimeoffset` | |
| `Details` | `nvarchar(max)` | JSON — action-specific metadata |

---

## 13. Key NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Graph` | 5.x | Graph SDK — fluent API for all SP/SPE operations |
| `Azure.Identity` | 1.x | `DefaultAzureCredential`, `ManagedIdentityCredential` |
| `Polly` | 8.x | Resilience pipelines: retry, circuit breaker, bulkhead |
| `Microsoft.Extensions.Http.Resilience` | 8.x | Polly integration with `IHttpClientFactory` |
| `Microsoft.AspNetCore.SignalR` | 8.x | Upload progress push to UI |
| `Microsoft.EntityFrameworkCore.SqlServer` | 8.x | Client config + audit store |
| `Serilog.AspNetCore` | 8.x | Structured logging → App Insights / log aggregator |
| `OpenTelemetry.Extensions.Hosting` | 1.x | Distributed tracing across SP/SPE operations |

> **No MediatR** — commands and queries are dispatched via explicit handler classes
> registered in DI. `StorageAdapterResolver` is the only runtime dispatch mechanism.

---

## 14. Implementation Phases

### Phase 1 — Foundation
- `Core` project: all interfaces, entities, enums, models
- `GraphClientFactory` with `DefaultAzureCredential` / Managed Identity
- `SharePointAdapter` — folder CRUD, single-file upload, list, version history, online edit URL
- Polly throttling pipeline + `GraphBatchExecutor`
- `StorageAdapterResolver` skeleton (SP only at this stage)

### Phase 2 — Permissions & Provisioning
- `SharePointPermissionService` — break inheritance, group grant, delta token tracking
- `FolderProvisioningService` — walks `FolderStructureSpec`, creates tree idempotently
- Admin API endpoints — provision site, grant permissions, view library tree
- `ProvisionClientSiteHandler`

### Phase 3 — SPE Support
- `SharePointEmbeddedAdapter` — full `IDocumentStorageAdapter` implementation for SPE
- `SpePermissionService`
- `StorageAdapterResolver` updated for dual-mode dispatch
- Admin portal: SPE container creation, backend toggle UI

### Phase 4 — Parallel Worker & Upload Progress
- `BatchUploadWorker` — `Channel<UploadRequest>` producer/consumer
- `GraphUploadSessionManager` — resumable large-file uploads
- `PermissionSyncWorker` — delta query-based drift detection
- SignalR `UploadProgressHub`

### Phase 5 — Excel Integration
- `ExcelWorkbookService` — read/write via Graph `/workbook` session pattern
- API endpoints and UI for workbook read/write operations

### Phase 6 — Migration Scripts
- **Script A** (PowerShell): Grant MI `Sites.Selected` to all existing SP sites
- **Script B** (PowerShell): Enumerate + copy SP → SPE with resumable state tracking

### Phase 7 — Hardening & Observability
- OpenTelemetry traces for all Graph calls (span per batch, per upload)
- Load testing at scale: thousands of clients, concurrent batch uploads
- Per-client bulkhead tuning based on load test results
- Circuit breaker thresholds calibrated against Graph throttling limits

---

## 15. File & Folder Conventions

| Convention | Rule |
|---|---|
| **Namespace** | Matches folder path: `SharepointDocManager.Infrastructure.Graph` |
| **Interfaces** | Prefixed with `I`, one per file, in `Core/Interfaces/` |
| **Adapters** | Named `{Backend}Adapter.cs`, implement `IDocumentStorageAdapter` |
| **Handlers** | Named `{Command/Query}Handler.cs`, paired with their command/query file |
| **No `Migration` prefix on .NET classes** | Migration = PowerShell only. .NET classes use domain names. |
| **appsettings** | Environment-specific overrides via `appsettings.{Environment}.json` |
| **Secrets** | Never in appsettings. Always via environment variables or Key Vault references. |
| **Tests** | Mirror the `src/` structure. Unit tests mock `IDocumentStorageAdapter`. Integration tests use a real Graph dev tenant. |

---

*Last updated: 2026-04-06*
