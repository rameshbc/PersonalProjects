# Azure Service Bus Message Library — Specification & Plan

**Project:** `MessagingLibrary` (reusable .NET library + worker service scaffold)
**Date:** 2026-03-22
**Status:** Draft v2

---

## 1. Goals & Non-Goals

### Goals
- Single reusable .NET 8 library (`Messaging.Core`) wrapping Azure Service Bus (queues and topics/subscriptions) with **audit built in** — worker services pass audit DB config via `MessagingOptions`; no separate package to install
  - Namespace is provider-agnostic (`Messaging.*`) so future providers (RabbitMQ, etc.) can be added without breaking consumers
- Best-practice resilience: retry policies, dead-letter handling, poison-message detection
- Pre-publish pending-message check using the **audit DB** (not Service Bus admin API) — keyed by client + destination + message type; no added latency or Service Bus API rate-limit risk
- Structured DB audit log: client identity, service, operation, queue/topic, message body (optionally compressed), and rich status lifecycle
- Worker hosting adapters for:
  - **Container App** — KEDA-based autoscaling driven by queue/topic length
  - **Windows Service** — SCM-compatible host; multiple instances pointing to same queue/topic

### Non-Goals
- Replacing Azure Service Bus management-plane operations (namespace provisioning, ARM)
- Supporting non-Azure brokers in v1 (namespace is designed to accommodate them in v2)
- Full saga/workflow orchestration engine

---

## 2. Functional Requirements

### 2.1 `Messaging.Core` — Messaging + Audit (single library)

Audit is **not a separate package**. Worker services supply the audit DB connection string inside `MessagingOptions`; the library wires everything internally via a single `AddServiceBusMessaging(...)` call.

**Messaging**

| # | Requirement |
|---|-------------|
| F1 | Publish a message to a **queue** or **topic** |
| F2 | Receive messages from a **queue** or topic **subscription** (processor-based, push model) |
| F3 | Configurable retry policy: exponential back-off with jitter, max retries, max delivery count |
| F4 | Dead-letter support: explicit dead-letter with reason/description; configurable DLQ monitoring |
| F5 | Session support (ordered message groups) — optional, per-destination |
| F6 | Duplicate detection via MessageId (idempotency key) |
| F7 | Message compression/decompression: opt-in per-message or per-client using GZip; stored as byte[] payload with metadata flag |
| F8 | Pre-publish pending check: query `MessageAuditLog` DB for active messages matching (ClientId + DestinationName + Subject); suppress publish and return `PublishResult.Suppressed` if count ≥ threshold — see §2.3 |
| F9 | Scheduled/deferred message publishing |
| F10 | Graceful shutdown: drain in-flight messages before stopping processor |
| F11 | Configurable receive mode per worker: **Push** (processor-based, broker pushes one at a time) or **PullBatch** (worker explicitly pulls 1–N messages per cycle) — see §2.4 |
| F12 | Configurable processing mode per worker: **Sequential** (one message at a time) or **Parallel** (concurrent up to `MaxDegreeOfParallelism`) — applies to both receive modes — see §2.4 |

**Audit (built into `Messaging.Core`)**

| # | Requirement |
|---|-------------|
| A1 | Persist every publish and receive event to a relational DB table (`MessageAuditLog`) |
| A2 | Columns: Id, ClientId, ServiceName, HostName, OperationType, DestinationType, DestinationName, MessageId, CorrelationId, Subject, Body (compressed blob, nullable), IsBodyCompressed, BodySizeBytes, Status, StatusDetail, PendingCount (snapshot), CreatedAt, UpdatedAt |
| A3 | Status lifecycle: `Queued → Published | PublishFailed | Suppressed` (publisher) and `Received → Processing → Completed | Failed → DeadLettered` (receiver) |
| A4 | EF Core (SQL Server / PostgreSQL) with `IDbContextFactory` pooling; `IAuditRepository` interface allows custom backend if ever needed |
| A5 | Async, fire-and-forget via `Channel<T>` drain — never blocks the message path; failures logged to `ILogger` only |
| A6 | Configurable per worker: log body on/off, max body bytes stored, retention days, pending check threshold |

### 2.3 Pre-Publish Pending-Message Check (DB-Based)

**Design decision: DB query, not Service Bus admin API.**

Rationale:
- Calling `ServiceBusAdministrationClient.GetQueueRuntimePropertiesAsync` per publish adds ~50–200 ms network round-trip and is subject to Service Bus API rate limits. For high-throughput publishers or multiple clients this is unacceptable.
- The audit DB is always written first (local network); the query is a simple indexed COUNT on an active status column — sub-millisecond under normal load.
- The check is **per-client** — `ClientId` is read from `MessageEnvelope.ClientId` (set by the caller on each message, not from app config), allowing different publishing clients to share the same queue without interfering with each other's suppression thresholds.

**Check key:** `(envelope.ClientId, DestinationName, Subject?)` — `ClientId` and `DestinationName` are always required; `Subject` is optional for per-message-type suppression within the same queue.

**Flow:**

```
PublishAsync called
       │
       ▼
Is PendingCheckEnabled?  ──No──► Skip to publish
       │ Yes
       ▼
SELECT COUNT(*) FROM MessageAuditLog
 WHERE ClientId       = @clientId
   AND DestinationName = @destinationName
   AND Subject         = @subject          -- if configured
   AND Status IN ('Queued','Published','Received','Processing')
   AND CreatedAt       > SYSUTCDATETIME() - @lookbackWindow
       │
 Count ≥ MaxPendingBeforeSuppress?
   Yes ──► Write Suppressed audit row, return PublishResult.Suppressed
   No  ──► Continue to publish
```

**Configuration:**

```jsonc
"PendingCheck": {
  "Enabled": true,
  "MaxPendingBeforeSuppress": 10,    // per (ClientId, DestinationName, Subject)
  "LookbackWindowMinutes": 60        // only count messages younger than N min
}
```

**PublishResult:**

```csharp
public sealed record PublishResult(
    PublishStatus Status,          // Published | Suppressed | PublishFailed
    string? MessageId,
    long? PendingCount,            // DB count snapshot (replaces ActiveMessageCount)
    string? SuppressReason,
    Exception? Exception);
```

### 2.4 Receive Mode & Processing Mode

Two orthogonal settings — **how to fetch** and **how to process** — configured independently per worker.

#### Receive Mode

| Mode | Mechanism | Best for |
|---|---|---|
| `Push` | `ServiceBusProcessor` — broker pushes messages one at a time via callback | Steady streams, low-latency, simple setup |
| `PullBatch` | `ServiceBusReceiver.ReceiveMessagesAsync(maxMessages: N)` — worker explicitly pulls up to N messages per cycle, then loops | Controlled batch processing; explicit per-batch transactionality |

#### Processing Mode

| Mode | Behaviour | Config |
|---|---|---|
| `Sequential` | Handle one message fully before the next | Push: `MaxConcurrentCalls = 1`; PullBatch: `foreach` + `await` |
| `Parallel` | Handle multiple messages concurrently | Push: `MaxConcurrentCalls = MaxDegreeOfParallelism`; PullBatch: `Parallel.ForEachAsync` capped at `MaxDegreeOfParallelism` |

#### Combinations

```
ReceiveMode = Push,      ProcessingMode = Sequential  → broker pushes 1, handler awaited, then next
ReceiveMode = Push,      ProcessingMode = Parallel    → broker pushes up to MaxDegreeOfParallelism concurrently
ReceiveMode = PullBatch, ProcessingMode = Sequential  → pull up to BatchSize, process one by one, loop
ReceiveMode = PullBatch, ProcessingMode = Parallel    → pull up to BatchSize, Task.WhenAll / Parallel.ForEachAsync, loop
```

> `PrefetchCount` applies to both modes — it pre-loads messages into local memory buffer ahead of processing, reducing broker round-trips.

#### PullBatch loop (internal library behaviour)

```
while (!cancellationToken.IsCancellationRequested)
{
    messages = await receiver.ReceiveMessagesAsync(
                   maxMessages:  options.BatchSize,        // 1..N
                   maxWaitTime:  options.BatchWaitTimeout, // how long to block waiting for messages
                   ct);

    if (!messages.Any()) continue;   // no messages; back-off and retry

    if (options.ProcessingMode == Sequential)
        foreach (msg in messages)  await HandleOne(msg, ct);
    else
        await Parallel.ForEachAsync(messages,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism },
            async (msg, ct) => await HandleOne(msg, ct));
}
```

### 2.5 Container App Scaling (KEDA)

- KEDA `ScaledObject` manifest generated or documented per worker
- Triggers: `azure-servicebus` (queue length or topic subscription length)
- Min replicas: 0 (scale-to-zero), Max replicas: configurable
- Library exposes Prometheus/OpenTelemetry metrics that KEDA can optionally scrape
- Worker `Dockerfile` and `docker-compose` provided as scaffold

### 2.6 Windows Service Hosting

- Worker uses `UseWindowsService()` host extension
- Service name, display name, description configurable via `appsettings.json`
- Multiple service instances can share the same queue/topic by having separate SCM registrations with distinct `ServiceName` values
- `ServiceName` + `ClientId` used as audit log columns to distinguish instances
- Install/uninstall script (`sc.exe` wrapper PowerShell) provided as scaffold

### 2.7 Multi-Instance Safety

- Queue: competing-consumer pattern — no extra coordination needed; each instance processes a subset
- Topic subscription: each worker gets its **own named subscription** (fan-out) or shares one subscription (competing consumer) — configurable
- `MessageLockRenewalEnabled`: library auto-renews lock for long-running handlers

---

## 3. Non-Functional Requirements

| # | Requirement |
|---|-------------|
| NF1 | Target: **.NET 8** class library |
| NF2 | Thread-safe; supports concurrent `ServiceBusProcessor` instances |
| NF3 | Zero-allocation hot path where possible; Span\<byte\> for compression |
| NF4 | All public APIs are `async`/`await` with `CancellationToken` |
| NF5 | OpenTelemetry traces and metrics via `System.Diagnostics.ActivitySource` |
| NF6 | `ILogger<T>` throughout; structured logging |
| NF7 | Fully unit-testable via interfaces; no static state |
| NF8 | NuGet-packageable; semantic versioning |

---

## 4. Solution Structure

```
WorkerServiceContainerWithScaler/
├── src/
│   ├── Messaging.Core/                           # Single library — messaging + audit
│   │   ├── Abstractions/
│   │   │   ├── IMessagePublisher.cs
│   │   │   ├── IMessageReceiver.cs
│   │   │   ├── IMessageHandler.cs
│   │   │   └── IAuditRepository.cs               # Allows custom audit backend if needed
│   │   ├── Models/
│   │   │   ├── MessageEnvelope.cs                # Wraps payload + metadata
│   │   │   ├── PublishOptions.cs
│   │   │   ├── PublishResult.cs
│   │   │   ├── PublishStatus.cs                  # Enum: Published | Suppressed | PublishFailed
│   │   │   ├── ReceiveOptions.cs
│   │   │   └── MessageStatus.cs                  # Enum: Queued | Published | PublishFailed | Suppressed |
│   │   │                                         #        Received | Processing | Completed | Failed | DeadLettered
│   │   ├── Publishers/
│   │   │   ├── ServiceBusQueuePublisher.cs
│   │   │   └── ServiceBusTopicPublisher.cs
│   │   ├── Receivers/
│   │   │   ├── ServiceBusQueueReceiver.cs
│   │   │   └── ServiceBusTopicReceiver.cs
│   │   ├── Resilience/
│   │   │   ├── RetryPolicyOptions.cs
│   │   │   └── ServiceBusResiliencePipeline.cs   # Polly v8 pipeline
│   │   ├── Compression/
│   │   │   ├── IPayloadCompressor.cs
│   │   │   └── GZipPayloadCompressor.cs
│   │   ├── Audit/                                # Audit — part of Core, not a separate project
│   │   │   ├── Models/
│   │   │   │   └── MessageAuditLog.cs            # EF Core entity
│   │   │   ├── Repositories/
│   │   │   │   └── EfCoreAuditRepository.cs      # Implements IAuditRepository + pending check
│   │   │   ├── DbContext/
│   │   │   │   └── MessagingAuditDbContext.cs
│   │   │   ├── Migrations/                       # EF Core migrations
│   │   │   ├── CompiledQueries.cs                # EF.CompileAsyncQuery definitions
│   │   │   └── AuditLogger.cs                    # Fire-and-forget Channel<T> drain
│   │   ├── DI/
│   │   │   └── ServiceCollectionExtensions.cs    # AddServiceBusMessaging(...) — wires both messaging + audit
│   │   └── Options/
│   │       ├── MessagingOptions.cs               # Top-level options incl. AuditOptions nested inside
│   │       └── AuditOptions.cs                   # Audit sub-options (connection string, body logging, pending check)
│   │
│   ├── WorkerHost.Core/                     # Hosting helpers
│   │   ├── BackgroundServiceBase.cs              # Base IHostedService with DI wiring
│   │   ├── MessageHandlerHostedService.cs        # Generic hosted service
│   │   └── DI/
│   │       └── WorkerHostExtensions.cs
│   │
│   └── WorkerHost.Windows/             # Windows Service extras
│       └── WindowsServiceConfigurator.cs
│
├── samples/
│   ├── SampleWorker.Queue/                       # Queue consumer worker
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Dockerfile
│   │   └── Worker.cs
│   ├── SampleWorker.Topic/                       # Topic/subscription consumer
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   └── Worker.cs
│   └── SamplePublisher.Api/                      # Minimal API that publishes
│       ├── Program.cs
│       └── appsettings.json
│
├── deploy/
│   ├── keda/
│   │   ├── scaledobject-queue.yaml               # KEDA ScaledObject for queue
│   │   └── scaledobject-topic.yaml               # KEDA ScaledObject for topic
│   ├── windows-service/
│   │   ├── install-service.ps1
│   │   └── uninstall-service.ps1
│   └── sql/
│       └── 001_CreateMessageAuditLog.sql         # Standalone migration fallback
│
└── tests/
    ├── Messaging.Core.Tests/                     # Unit tests for messaging + audit together
    └── Messaging.Integration.Tests/              # Against real/emulated Service Bus
```

---

## 5. Key Designs

### 5.1 MessageEnvelope

```csharp
public sealed class MessageEnvelope
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Identity of the publishing client — set by the caller on each message, NOT from app config.
    /// Stamped as Service Bus application property "x-messaging-client-id" on send.
    /// Extracted from that property on receive so the audit log always knows the origin.
    /// Used as the primary key for the pending-message check.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    public string? CorrelationId { get; init; }
    public string? Subject { get; init; }         // message-type discriminator; used in pending check
    public string? ContentType { get; init; }     // "application/json+gzip" when compressed
    public bool IsCompressed { get; init; }
    public ReadOnlyMemory<byte> Body { get; init; }
    public IReadOnlyDictionary<string, object> ApplicationProperties { get; init; }
    public DateTimeOffset? ScheduledEnqueueTime { get; init; }
    public string? SessionId { get; init; }
}
```

> **Why on the message, not in config?**
> Multiple different publishing clients can share the same queue. Stamping `ClientId` per-message means the receiver and audit log always know which system sent each message, without any receiver-side configuration. The pending-message check is then scoped to `(envelope.ClientId, DestinationName, Subject?)` — each client is governed by its own backlog threshold independently.

### 5.2 IMessagePublisher

```csharp
public interface IMessagePublisher
{
    Task<PublishResult> PublishAsync<T>(
        string destinationName,     // queue name or topic name
        T payload,
        PublishOptions? options = null,
        CancellationToken ct = default);

    Task<PublishResult> PublishAsync(
        string destinationName,
        MessageEnvelope envelope,
        PublishOptions? options = null,
        CancellationToken ct = default);
}
```

### 5.3 IPendingMessageChecker

```csharp
/// <summary>
/// DB-based check. Implemented by Messaging.Audit; registered via AddMessagingAudit().
/// Messaging.Core only depends on this interface — no direct DB coupling in core.
/// </summary>
public interface IPendingMessageChecker
{
    Task<PendingCheckResult> CheckAsync(
        string clientId,
        string destinationName,
        string? subject,
        CancellationToken ct = default);
}

public sealed record PendingCheckResult(bool ShouldSuppress, long PendingCount);
```

### 5.4 IMessageHandler (implement in worker)

```csharp
public interface IMessageHandler<T>
{
    Task HandleAsync(T message, MessageContext context, CancellationToken ct);
}

public sealed class MessageContext
{
    public string MessageId { get; init; }
    public string? CorrelationId { get; init; }
    public string DestinationName { get; init; }   // queue or topic/subscription
    public ServiceBusReceivedMessage RawMessage { get; init; }
    public Func<Task> CompleteAsync { get; init; }           // settle: complete
    public Func<string, string, Task> DeadLetterAsync { get; init; } // settle: DLQ
    public Func<TimeSpan, Task> AbandonAsync { get; init; }  // settle: retry
}
```

### 5.5 ReceiveOptions

```csharp
public sealed class ReceiveOptions
{
    /// <summary>Push = ServiceBusProcessor; PullBatch = explicit ReceiveMessagesAsync loop.</summary>
    public ReceiveMode ReceiveMode { get; set; } = ReceiveMode.Push;

    /// <summary>
    /// PullBatch only: maximum messages fetched per cycle (1–N).
    /// Push mode ignores this — use MaxDegreeOfParallelism instead.
    /// </summary>
    public int BatchSize { get; set; } = 1;

    /// <summary>PullBatch only: how long to block waiting for messages before looping again.</summary>
    public TimeSpan BatchWaitTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Sequential = one at a time; Parallel = concurrent up to MaxDegreeOfParallelism.</summary>
    public ProcessingMode ProcessingMode { get; set; } = ProcessingMode.Sequential;

    /// <summary>
    /// Parallel mode: max concurrent handler invocations.
    /// Push mode:     maps directly to ServiceBusProcessor.MaxConcurrentCalls.
    /// PullBatch mode: caps Parallel.ForEachAsync concurrency.
    /// Ignored when ProcessingMode = Sequential.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// Messages pre-fetched into local buffer ahead of processing.
    /// Applies to both Push and PullBatch modes.
    /// Rule of thumb: BatchSize * MaxDegreeOfParallelism * 2.
    /// </summary>
    public int PrefetchCount { get; set; } = 10;
}

public enum ReceiveMode   { Push, PullBatch }
public enum ProcessingMode { Sequential, Parallel }
```

### 5.6 Resilience Pipeline (Polly v8)

```
Retry (exponential back-off + jitter, configurable max attempts)
  → Circuit Breaker (open after N consecutive failures in T seconds)
    → Timeout (per-attempt)
```

- Retry on transient `ServiceBusException` (server busy, timeout, quota exceeded)
- Do NOT retry on `MessageLockLostException` or `SessionLockLostException` → abandon immediately
- Publish failures after all retries exhausted → `PublishStatus.PublishFailed` + audit row

### 5.7 MessageAuditLog Table

`ClientId` is the key that lets multiple callers publish to the same queue independently.
The pending-message check is always scoped to `(ClientId, DestinationName, Subject?)`.

```sql
CREATE TABLE MessageAuditLog (
    Id                BIGINT          IDENTITY PRIMARY KEY,
    ClientId          NVARCHAR(128)   NOT NULL,   -- from MessageEnvelope.ClientId (set per-message by publisher, not from config)
    ServiceName       NVARCHAR(256)   NOT NULL,   -- logical service name
    HostName          NVARCHAR(256)   NOT NULL,   -- machine / pod name
    OperationType     NVARCHAR(32)    NOT NULL,   -- Publish | Receive | DeadLetter | Suppress
    DestinationType   NVARCHAR(16)    NOT NULL,   -- Queue | Topic | Subscription
    DestinationName   NVARCHAR(260)   NOT NULL,   -- queue name OR "topic/subscription" composite
    MessageId         NVARCHAR(128)   NULL,
    CorrelationId     NVARCHAR(128)   NULL,
    Subject           NVARCHAR(512)   NULL,       -- message type / subject (used in pending check)
    Body              VARBINARY(MAX)  NULL,       -- compressed or raw JSON bytes
    IsBodyCompressed  BIT             NOT NULL DEFAULT 0,
    BodySizeBytes     INT             NULL,
    Status            NVARCHAR(32)    NOT NULL,
    --   Publisher statuses: Queued | Published | PublishFailed | Suppressed
    --   Receiver statuses:  Received | Processing | Completed | Failed | DeadLettered
    StatusDetail      NVARCHAR(1024)  NULL,
    PendingCount      BIGINT          NULL,       -- DB count snapshot at pre-publish check
    CreatedAt         DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt         DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Indexes
-- Primary query for pending check: (ClientId, DestinationName, Subject, Status, CreatedAt)
CREATE INDEX IX_Audit_PendingCheck
    ON MessageAuditLog(ClientId, DestinationName, Subject, Status, CreatedAt DESC)
    INCLUDE (Id);

CREATE INDEX IX_Audit_Destination_Status
    ON MessageAuditLog(DestinationName, Status, CreatedAt DESC);

CREATE INDEX IX_Audit_MessageId
    ON MessageAuditLog(MessageId);

CREATE INDEX IX_Audit_CreatedAt
    ON MessageAuditLog(CreatedAt DESC);
```

### 5.8 EF Core Query Strategy (No Dynamic SQL)

**Rules — enforced by code review and analyzer:**

| Rule | Detail |
|---|---|
| No raw SQL | `FromSqlRaw`, `FromSqlInterpolated`, `ExecuteSqlRaw`, `ExecuteSqlInterpolated` are banned. All queries use strongly-typed LINQ only. |
| No dynamic predicate building | No string concatenation to form filter expressions; use explicit `if` branches for optional filters (e.g. nullable `Subject`). |
| Compiled queries for hot paths | The pending check COUNT runs before every publish. Use `EF.CompileAsyncQuery` so the query plan is built once at startup. |
| `AsNoTracking` on all reads | Audit reads and pending checks never need change tracking; always call `.AsNoTracking()`. |
| Pooled `IDbContextFactory<T>` | Register with `AddPooledDbContextFactory<MessagingAuditDbContext>`. Each audit write or pending check rents a context from the pool and returns it — no singleton `DbContext`. |
| Write path is append-only | `MessageAuditLog` rows are only ever inserted (`AddAsync`) or updated via a typed `ExecuteUpdateAsync` (no `SaveChanges` on detached entities). |
| Indexes declared in `OnModelCreating` | All indexes are defined in `HasIndex(...)` fluent config — single source of truth; migrations derive from them. |
| Status stored as `string` with `ValueConverter` | Enum → string via `HasConversion<string>()` so the column is human-readable without a lookup table; no magic integer codes. |

**Compiled pending-check query (defined once at startup):**

```csharp
// Registered as singleton in DI
internal static class CompiledQueries
{
    // With Subject filter
    internal static readonly Func<MessagingAuditDbContext, string, string, string, DateTime, int, Task<int>>
        PendingCountWithSubject = EF.CompileAsyncQuery(
            (MessagingAuditDbContext db,
             string clientId,
             string destinationName,
             string subject,
             DateTime cutoff,
             int _unused) =>
                db.MessageAuditLogs
                  .AsNoTracking()
                  .Count(x =>
                      x.ClientId        == clientId        &&
                      x.DestinationName == destinationName &&
                      x.Subject         == subject         &&
                      (x.Status == MessageStatus.Queued      ||
                       x.Status == MessageStatus.Published   ||
                       x.Status == MessageStatus.Received    ||
                       x.Status == MessageStatus.Processing) &&
                      x.CreatedAt       >= cutoff));

    // Without Subject filter (Subject not configured)
    internal static readonly Func<MessagingAuditDbContext, string, string, DateTime, int, Task<int>>
        PendingCountNoSubject = EF.CompileAsyncQuery(
            (MessagingAuditDbContext db,
             string clientId,
             string destinationName,
             DateTime cutoff,
             int _unused) =>
                db.MessageAuditLogs
                  .AsNoTracking()
                  .Count(x =>
                      x.ClientId        == clientId        &&
                      x.DestinationName == destinationName &&
                      (x.Status == MessageStatus.Queued      ||
                       x.Status == MessageStatus.Published   ||
                       x.Status == MessageStatus.Received    ||
                       x.Status == MessageStatus.Processing) &&
                      x.CreatedAt       >= cutoff));
}
```

**`OnModelCreating` — indexes and conventions:**

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    var audit = modelBuilder.Entity<MessageAuditLog>();

    audit.ToTable("MessageAuditLog");
    audit.HasKey(x => x.Id);

    // Pending check hot-path index
    audit.HasIndex(x => new { x.ClientId, x.DestinationName, x.Subject, x.Status, x.CreatedAt })
         .HasDatabaseName("IX_Audit_PendingCheck")
         .IsDescending(false, false, false, false, true);   // CreatedAt DESC

    audit.HasIndex(x => new { x.DestinationName, x.Status, x.CreatedAt })
         .HasDatabaseName("IX_Audit_Destination_Status")
         .IsDescending(false, false, true);

    audit.HasIndex(x => x.MessageId)
         .HasDatabaseName("IX_Audit_MessageId");

    audit.HasIndex(x => x.CreatedAt)
         .HasDatabaseName("IX_Audit_CreatedAt")
         .IsDescending(true);

    // Enum → string (human-readable, no lookup table)
    audit.Property(x => x.Status)
         .HasConversion<string>()
         .HasMaxLength(32);

    audit.Property(x => x.DestinationType)
         .HasConversion<string>()
         .HasMaxLength(16);
}
```

**Audit write (append-only insert, never update existing rows in-place):**

```csharp
// Status updates are a new insert with UpdatedAt, not an in-place mutation,
// so the audit trail is fully append-only and queryable by status over time.
// Exception: UpdatedAt on the *same* row is only touched by the background
// Channel<T> drain using ExecuteUpdateAsync — no tracked entity needed.

await db.MessageAuditLogs
        .Where(x => x.Id == logId)
        .ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.Status,      newStatus)
             .SetProperty(x => x.StatusDetail, detail)
             .SetProperty(x => x.UpdatedAt,   DateTime.UtcNow),
            ct);
```

### 5.9 Authentication — Managed Identity & Token Management

**Production deployments must use Managed Identity, not connection strings with SAS keys.**

#### Auth modes

| Mode | When | Mechanism |
|---|---|---|
| `ConnectionString` | Local dev / integration tests | `ServiceBusClient(connectionString)` |
| `ManagedIdentity` | Production (Container App, VM, Windows Service) | `ServiceBusClient(fullyQualifiedNamespace, TokenCredential)` |

#### Token management — no manual refresh needed

The Azure Service Bus SDK (`Azure.Messaging.ServiceBus`) and `Azure.Identity` handle token lifecycle automatically:
- `DefaultAzureCredential` probes the credential chain in order: environment vars → workload identity → managed identity → Azure CLI (dev) → Visual Studio (dev)
- Tokens are cached and silently refreshed before expiry by the SDK — the library does not need to manage this
- If a token refresh fails transiently, the SDK retries; the Polly circuit breaker in the library wraps the outer send operation

#### RBAC roles required on the Service Bus namespace

| Role | Required for |
|---|---|
| `Azure Service Bus Data Sender` | Publishers |
| `Azure Service Bus Data Receiver` | Consumers / workers |
| `Azure Service Bus Data Owner` | Only if library creates/manages queues or subscriptions at runtime |

#### Options model

```csharp
public sealed class MessagingOptions
{
    public ServiceBusAuthMode AuthMode { get; set; } = ServiceBusAuthMode.ConnectionString;

    // ConnectionString mode only
    public string? ConnectionString { get; set; }

    // ManagedIdentity mode only
    public string? FullyQualifiedNamespace { get; set; }   // e.g. "myns.servicebus.windows.net"
    public string? ManagedIdentityClientId { get; set; }   // null = system-assigned; set for user-assigned

    public string  ServiceName { get; set; } = string.Empty;
    // ClientId intentionally omitted — comes from each MessageEnvelope, not global config
    // ... other options
}

public enum ServiceBusAuthMode { ConnectionString, ManagedIdentity }
```

#### DI wiring — credential selection

```csharp
// Inside AddServiceBusMessaging — not called by consumer directly
TokenCredential credential = options.AuthMode == ServiceBusAuthMode.ManagedIdentity
    ? (options.ManagedIdentityClientId is not null
        ? new ManagedIdentityCredential(options.ManagedIdentityClientId)   // user-assigned
        : new DefaultAzureCredential())                                     // system-assigned or dev chain
    : null!;

ServiceBusClient client = options.AuthMode == ServiceBusAuthMode.ManagedIdentity
    ? new ServiceBusClient(options.FullyQualifiedNamespace!, credential)
    : new ServiceBusClient(options.ConnectionString!);

services.AddSingleton(client);
```

> `ServiceBusClient` is thread-safe and long-lived — registered as `Singleton`. Token refresh is internal to the SDK; the application never holds or refreshes a raw token.

#### KEDA — Managed Identity trigger auth

When using Managed Identity, KEDA cannot use a connection string. Use `TriggerAuthentication` with workload identity:

```yaml
apiVersion: keda.sh/v1alpha1
kind: TriggerAuthentication
metadata:
  name: keda-sb-auth
spec:
  podIdentity:
    provider: azure-workload    # AKS workload identity
    identityId: "<user-assigned-managed-identity-client-id>"   # omit for system-assigned
---
# ScaledObject trigger block
triggers:
  - type: azure-servicebus
    metadata:
      namespace: myns.servicebus.windows.net   # instead of connectionFromEnv
      queueName: orders-queue
      messageCount: "5"
    authenticationRef:
      name: keda-sb-auth
```

### 5.10 DI Registration

```csharp
// Program.cs — any worker or API
// Single call — messaging + audit configured together via MessagingOptions
builder.Services.AddServiceBusMessaging(options =>
{
    // Auth — production uses ManagedIdentity; dev uses ConnectionString
    options.AuthMode = ServiceBusAuthMode.ManagedIdentity;         // production
    options.FullyQualifiedNamespace = builder.Configuration["ServiceBus:Namespace"];
    // options.ManagedIdentityClientId = "...";                    // only for user-assigned MI
    // options.ConnectionString = builder.Configuration["ServiceBus:ConnectionString"]; // dev only

    // ClientId is NOT set here — it is stamped per-message by the publishing caller on MessageEnvelope
    options.ServiceName       = builder.Configuration["Messaging:ServiceName"];
    options.EnableCompression = true;
    options.DefaultRetryPolicy = new RetryPolicyOptions
    {
        MaxRetries = 3,
        BaseDelay  = TimeSpan.FromSeconds(2),
        MaxDelay   = TimeSpan.FromSeconds(30),
        UseJitter  = true
    };

    // Audit — nested inside the same options object; worker passes its own DB config
    options.Audit.ConnectionString    = builder.Configuration.GetConnectionString("AuditDb");
    options.Audit.LogMessageBody      = true;
    options.Audit.MaxBodyBytesStored  = 64 * 1024;   // 64 KB cap
    options.Audit.RetentionDays       = 90;
    options.Audit.PendingCheck.Enabled                  = true;
    options.Audit.PendingCheck.MaxPendingBeforeSuppress = 10;
    options.Audit.PendingCheck.LookbackWindowMinutes    = 60;
});

// Register a typed handler — destinationName is the queue or "topic/subscription"
builder.Services.AddMessageHandler<OrderCreatedMessage, OrderCreatedHandler>("orders-queue");
```

### 5.10 KEDA ScaledObject (Queue)

```yaml
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata:
  name: worker-queue-scaler
spec:
  scaleTargetRef:
    name: sample-worker-queue
  minReplicaCount: 0
  maxReplicaCount: 10
  pollingInterval: 15
  cooldownPeriod: 30
  triggers:
    - type: azure-servicebus
      metadata:
        namespace: myns.servicebus.windows.net   # Managed Identity — no connection string
        queueName: orders-queue
        messageCount: "5"       # messages per replica before scale-up
      authenticationRef:
        name: keda-sb-auth      # TriggerAuthentication with podIdentity: azure-workload (see §5.9)
```

### 5.11 Windows Service Registration

```powershell
# install-service.ps1
param(
    [string]$ServiceName = "WorkerSvc-OrdersQueue",
    [string]$ExePath     = "C:\Services\SampleWorker.Queue\SampleWorker.Queue.exe"
)

New-Service -Name $ServiceName `
            -BinaryPathName $ExePath `
            -DisplayName "Orders Queue Worker" `
            -Description "Processes orders from Azure Service Bus queue" `
            -StartupType Automatic

Start-Service -Name $ServiceName
```

```csharp
// Program.cs
Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = builder.Configuration["Messaging:ServiceName"]
                              ?? "MessagingWorker";
    })
    .ConfigureServices(services => { /* ... */ })
    .Build()
    .Run();
```

---

## 6. Status Lifecycle

### Publisher Side

```
Queued ──► Published  (success)
       ──► PublishFailed  (all retries exhausted — Service Bus rejected)
       ──► Suppressed     (pending check: too many active messages for this client+queue+subject)
```

### Receiver Side

```
Received
    │
Processing
    │
    ├──► Completed          (handler succeeded; message completed on broker)
    │
    └──► Failed             (handler threw exception; message abandoned back to queue)
              │
              ├──► [retry loop: delivery count < max → back to Received on next dequeue]
              │
              └──► DeadLettered  (delivery count ≥ max OR explicit DLQ call from handler)
```

### Full Status Table

| Status | Side | Set by | Meaning |
|---|---|---|---|
| `Queued` | Publisher | Library | Message object created, send not yet attempted |
| `Published` | Publisher | Library | Successfully delivered to Service Bus |
| `PublishFailed` | Publisher | Library | Send failed after all retries; not in broker |
| `Suppressed` | Publisher | Library | Pending check blocked publish; not sent |
| `Received` | Receiver | Library | Message dequeued from Service Bus; lock acquired |
| `Processing` | Receiver | Library | Handler invoked |
| `Completed` | Receiver | Handler | Handler succeeded; message settled (Complete) |
| `Failed` | Receiver | Library | Handler threw; message abandoned (retryable) |
| `DeadLettered` | Receiver | Handler / SB | Max delivery exceeded or explicit DLQ call |

> **Failed vs DeadLettered:** `Failed` is transient — message re-enters the queue for retry.
> `DeadLettered` is terminal — message moved to the dead-letter sub-queue for manual review.

---

## 7. Implementation Plan

### Phase 1 — `Messaging.Core` (Messaging + Audit together) (Week 1–3)
| Task | Details |
|---|---|
| P1.1 | Create solution + projects (`Messaging.Core`, `WorkerHost.Core`) |
| P1.2 | Define all interfaces and models (`IAuditRepository`, `PublishStatus`, `MessageStatus` enums) |
| P1.3 | Implement `GZipPayloadCompressor` with Span\<byte\> |
| P1.4 | Implement `ServiceBusQueuePublisher` + `ServiceBusTopicPublisher` with resilience pipeline |
| P1.5 | Design `MessageAuditLog` EF Core entity + `MessagingAuditDbContext` with all indexes in `OnModelCreating` |
| P1.6 | Create EF Core migration + standalone SQL script |
| P1.7 | Implement `EfCoreAuditRepository` (async, pooled `IDbContextFactory`) + `CompiledQueries` |
| P1.8 | Implement fire-and-forget `AuditLogger` with `Channel<T>` background drain |
| P1.9 | Wire pending check (compiled COUNT query) into publisher pre-publish gate |
| P1.10 | Implement `ServiceBusQueueReceiver` + `ServiceBusTopicReceiver` with lock renewal |
| P1.11 | `ServiceCollectionExtensions` — single `AddServiceBusMessaging(...)` wiring messaging + audit |
| P1.12 | Unit tests for compression, pending check, publish/receive logic; EF Core SQLite tests for audit |

### Phase 2 — Worker Host + Samples (Week 3–4)
| Task | Details |
|---|---|
| P2.1 | `MessageHandlerHostedService<T>` base class |
| P2.2 | `SampleWorker.Queue` project with Dockerfile |
| P2.3 | `SampleWorker.Topic` project |
| P2.4 | `SamplePublisher.Api` (Minimal API with pending check demo) |
| P2.5 | `WorkerHost.Windows` with `UseWindowsService` wiring |
| P2.6 | Parameterised install/uninstall PowerShell scripts |

### Phase 3 — KEDA & Container Deploy (Week 4–5)
| Task | Details |
|---|---|
| P3.1 | KEDA `ScaledObject` YAML for queue trigger |
| P3.2 | KEDA `ScaledObject` YAML for topic/subscription trigger |
| P3.3 | Azure Container Apps `containerapp.yaml` scaffold |
| P3.4 | GitHub Actions CI pipeline (build, test, push image) |
| P3.5 | OpenTelemetry metrics (pending count, message throughput, handler duration) |

### Phase 4 — Integration Tests & Hardening (Week 5–6)
| Task | Details |
|---|---|
| P4.1 | Integration tests against Azure Service Bus emulator (or real namespace) |
| P4.2 | Dead-letter queue consumer and re-processing utility |
| P4.3 | Chaos tests: lock expiry, server busy, circuit-breaker open, DB unavailable (audit must not block) |
| P4.4 | Multi-client pending check correctness tests (ClientA suppressed, ClientB not) |
| P4.5 | Performance baseline: throughput, latency under load, pending check query time |
| P4.6 | NuGet packaging + versioning |

---

## 8. Configuration Reference

```jsonc
// appsettings.json  (production — Managed Identity)
// appsettings.Development.json  (dev — ConnectionString override)
// Single "Messaging" section — audit config is a nested sub-section
// NOTE: ClientId is NOT here — it is set per-message by the publishing caller on MessageEnvelope
{
  "Messaging": {
    // Auth: ManagedIdentity (production) or ConnectionString (dev/test)
    "AuthMode": "ManagedIdentity",
    "FullyQualifiedNamespace": "myns.servicebus.windows.net",  // ManagedIdentity mode
    // "ManagedIdentityClientId": "<guid>",                    // only for user-assigned MI
    // "ConnectionString": "<sb connection string>",           // ConnectionString mode (dev only)

    "ServiceName": "OrderService",
    "EnableCompression": true,
    "CompressionThresholdBytes": 1024,    // only compress if payload > 1 KB
    "RetryPolicy": {
      "MaxRetries": 3,
      "BaseDelaySeconds": 2,
      "MaxDelaySeconds": 30,
      "UseJitter": true
    },
    "CircuitBreaker": {
      "FailureThreshold": 5,
      "SamplingDurationSeconds": 30,
      "BreakDurationSeconds": 60
    },
    "LockRenewal": {
      "Enabled": true,
      "RenewalBufferSeconds": 10
    },
    // Audit — nested; worker supplies its own DB connection string
    "Audit": {
      "ConnectionString": "<SQL Server / PostgreSQL connection string>",
      "LogMessageBody": true,
      "MaxBodyBytesStored": 65536,
      "RetentionDays": 90,
      "PendingCheck": {
        "Enabled": true,
        "MaxPendingBeforeSuppress": 10,
        "LookbackWindowMinutes": 60
      }
    }
  },
  "Worker": {
    "QueueName": "orders-queue",

    // How to fetch messages from the broker
    "ReceiveMode": "PullBatch",        // Push | PullBatch
    "BatchSize": 10,                   // PullBatch only: messages pulled per cycle (1–N)
    "BatchWaitTimeout": "00:00:05",    // PullBatch only: wait up to 5s for messages per cycle

    // How to process fetched messages
    "ProcessingMode": "Parallel",      // Sequential | Parallel
    "MaxDegreeOfParallelism": 4,       // Parallel only: max concurrent handler calls

    // Pre-fetch buffer (both modes) — rule of thumb: BatchSize * MaxDegreeOfParallelism * 2
    "PrefetchCount": 80
  }
}
```

---

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Service Bus quota / throttling | Circuit breaker + retry with jitter; alert via OTel metric |
| Managed Identity token unavailable | `DefaultAzureCredential` retries internally; Polly circuit breaker wraps the outer publish; if MI is unreachable at startup, host fails fast with a clear error rather than silently using wrong identity |
| User-assigned MI client ID misconfigured | `ManagedIdentityCredential` throws `CredentialUnavailableException` at first use — caught at startup during health check, not silently at message time |
| Audit DB unavailable | Fire-and-forget `Channel<T>`; failures only log via `ILogger`; message path unblocked |
| **Pending check DB slow** | Covered by composite index `(ClientId, DestinationName, Subject, Status, CreatedAt)`; EF Core compiled query (no dynamic SQL); falls back to allow publish on DB timeout |
| **Pending check scope wrong** | ClientId + DestinationName + Subject triple scopes per-client correctly; multiple apps share same queue without interfering |
| Lock expiry on slow handlers | Auto lock renewal; configurable renewal buffer |
| Multiple Windows Service instances competing | Competing-consumer on same queue — expected; tune `BatchSize` + `MaxDegreeOfParallelism` per instance to share load evenly |
| Message body too large for DB | `MaxBodyBytesStored` cap; store hash only if over limit |
| KEDA scale-to-zero cold start latency | Set `minReplicaCount: 1` for latency-sensitive queues |
| Compression overhead for small payloads | Only compress if payload > `CompressionThresholdBytes` (default 1 KB) |
| `Failed` vs `DeadLettered` confusion | Both statuses always written to audit log with clear `StatusDetail`; `Failed` rows have a delivery-count field in `StatusDetail` |

---

## 10. Deliverables Summary

| Deliverable | Package / Path | Description |
|---|---|---|
| `Messaging.Core` | NuGet | Publisher, receiver, resilience, compression **+ built-in audit** (EF Core, pending check, `Channel<T>` drain) — single package |
| `WorkerHost.Core` | NuGet | Hosted service base, DI wiring |
| `WorkerHost.Windows` | NuGet | Windows Service SCM integration |
| Sample — Queue Worker | `samples/SampleWorker.Queue/` | Complete runnable sample with Dockerfile |
| Sample — Topic Worker | `samples/SampleWorker.Topic/` | Fan-out / subscription sample |
| Sample — Publisher API | `samples/SamplePublisher.Api/` | Minimal API with pending check result in response |
| KEDA manifests | `deploy/keda/` | ScaledObject YAML for queue and topic |
| Container Apps | `deploy/aca/` | `containerapp.yaml` scaffold |
| Windows deploy | `deploy/windows-service/` | install/uninstall PowerShell |
| SQL migration | `deploy/sql/` | EF Core migrations + standalone SQL fallback |
| Integration tests | `tests/Messaging.Integration.Tests/` | Full round-trip, chaos, multi-client pending check |
