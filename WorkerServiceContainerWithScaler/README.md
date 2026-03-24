# Messaging Library — Worker Service with Azure Service Bus

A .NET 8 messaging library providing a consistent abstraction over Azure Service Bus for publishing and consuming messages, with SQL Server audit logging, resilience pipelines, and KEDA-ready worker hosting.

---

## Solution Structure

```
MessagingLibrary.sln
├── src/
│   ├── Messaging.Core          # Core library — publisher, receiver, audit, compression, resilience
│   ├── WorkerHost.Core         # IHostedService base for queue/topic workers
│   └── WorkerHost.Windows      # Windows Service hosting wrapper
├── samples/
│   ├── SamplePublisher.Api     # Minimal API — POST /orders → publishes to orders-queue
│   ├── SampleWorker.Queue      # Worker — consumes orders-queue (PullBatch/Parallel)
│   └── SampleWorker.Topic      # Worker — consumes order-events topic/shipping-sub
├── tests/
│   ├── Messaging.Core.Tests    # Unit tests (xUnit, Moq, EF InMemory/SQLite)
│   └── Messaging.Integration.Tests
├── deploy/
│   ├── sql/                    # SQL migration — creates MessageAuditLog table
│   ├── servicebus-emulator/    # Config.json for local Service Bus Emulator
│   ├── keda/                   # KEDA ScaledObject manifests (queue + topic)
│   └── windows-service/        # PowerShell install/uninstall scripts
└── .devcontainer/              # Dev container (SQL Server + Service Bus Emulator)
```

---

## Key Features

| Feature | Detail |
|---|---|
| **Publish** | `IMessagePublisher.PublishAsync` — queue or topic, with optional compression |
| **Receive** | Push mode (processor) or PullBatch mode (sequential or parallel) |
| **Audit log** | Every publish and receive is written to `MessageAuditLog` in SQL Server |
| **CorrelationId** | Flows from HTTP header → Service Bus → worker audit row as a GUID |
| **Resilience** | Polly retry + circuit breaker on the publish path |
| **Lock renewal** | Background loop renews message locks before expiry |
| **Pending check** | Suppresses duplicate publishes when too many messages are already in-flight |
| **Compression** | Optional GZip compression for large payloads |
| **Auth modes** | `ConnectionString` (dev/emulator) or `ManagedIdentity` (production) |
| **KEDA** | `ScaledObject` manifests for queue-length and topic-length autoscaling |
| **Windows Service** | `WorkerHost.Windows` wraps any worker as a Windows Service |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- `docker-compose` (bundled with Docker Desktop)

---

## Local Development

### 1. Configure environment

```bash
cp .env.example .env
# .env already has defaults that work out of the box for local dev
```

### 2. Start infrastructure

```bash
docker-compose up -d sqlserver servicebus-emulator db-init
```

This starts:
- **SQL Server 2022** on `localhost:1433` — stores the audit log
- **Azure Service Bus Emulator** on `localhost:5672` — local Service Bus with `orders-queue` and `order-events` topic
- **db-init** (one-shot) — creates `MessagingAuditDev` database and `MessageAuditLog` table

> **Note:** The Service Bus Emulator requires `DefaultMessageTimeToLive` ≤ `PT1H`. The `deploy/servicebus-emulator/Config.json` is already configured correctly.

### 3. Run the worker

```bash
Messaging__ConnectionString="Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE=;UseDevelopmentEmulator=true;" \
ConnectionStrings__AuditDb="Server=localhost,1433;Database=MessagingAuditDev;User Id=sa;Password=YourStr0ng!DevPass;TrustServerCertificate=true;" \
dotnet run --project samples/SampleWorker.Queue
```

### 4. Run the publisher API

```bash
Messaging__ConnectionString="Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE=;UseDevelopmentEmulator=true;" \
ConnectionStrings__AuditDb="Server=localhost,1433;Database=MessagingAuditDev;User Id=sa;Password=YourStr0ng!DevPass;TrustServerCertificate=true;" \
dotnet run --project samples/SamplePublisher.Api
```

### 5. Publish a message

```bash
# With explicit CorrelationId (must be a valid GUID)
curl -X POST http://localhost:5223/orders \
  -H "Content-Type: application/json" \
  -H "X-Correlation-Id: $(uuidgen)" \
  -d '{"clientId":"my-client","customerId":"cust-001","amount":199.99}'

# Response: {"messageId":"<guid>"}  HTTP 202
```

---

## Dev Container

The `.devcontainer` configuration starts SQL Server and the Service Bus Emulator automatically and runs `post-create.sh` to restore packages.

Open in VS Code with the **Dev Containers** extension or in **JetBrains Rider** — both are configured in `devcontainer.json`.

---

## Configuration Reference

All settings live under the `Messaging` section in `appsettings.json` (non-secret) and environment variables / user-secrets (secrets).

```json
{
  "Messaging": {
    "AuthMode": "ConnectionString",
    "ServiceName": "OrderWorker",
    "EnableCompression": true,
    "CompressionThresholdBytes": 1024,
    "RetryPolicy": {
      "MaxRetries": 3,
      "BaseDelay": "00:00:02",
      "MaxDelay": "00:00:30",
      "UseJitter": true
    },
    "CircuitBreaker": {
      "FailureThreshold": 5,
      "SamplingDuration": "00:00:30",
      "BreakDuration": "00:01:00"
    },
    "LockRenewal": {
      "Enabled": true,
      "RenewalBufferSeconds": 10
    },
    "Audit": {
      "Enabled": true,
      "LogMessageBody": true,
      "MaxBodyBytesStored": 65536,
      "RetentionDays": 90,
      "PendingCheck": {
        "Enabled": true,
        "MaxPendingBeforeSuppress": 50,
        "LookbackWindowMinutes": 60
      }
    }
  }
}
```

| Secret | Where to set | Example |
|---|---|---|
| `Messaging:ConnectionString` | env var / user-secrets | `Endpoint=sb://...` |
| `Messaging:FullyQualifiedNamespace` | env var (prod) | `myns.servicebus.windows.net` |
| `ConnectionStrings:AuditDb` | env var / user-secrets | `Server=...;Database=MessagingAuditDev;...` |

---

## Audit Log

Every message is tracked end-to-end in `dbo.MessageAuditLog`:

| Status | Set by | Meaning |
|---|---|---|
| `Published` | Publisher | Message sent to Service Bus successfully |
| `Received` | Worker | Message pulled from Service Bus |
| `Processing` | Worker | Handler invoked |
| `Completed` | Worker | Handler succeeded, message completed on bus |
| `Failed` | Worker | Handler threw, message abandoned |
| `Suppressed` | Publisher | Pending-check threshold exceeded |

`MessageId` and `CorrelationId` are GUIDs on every row and match between the `Publish` and `Receive` rows for the same message, enabling full end-to-end tracing.

---

## Running Tests

```bash
dotnet test MessagingLibrary.sln
```

---

## Production Deployment

For AKS with managed identity, set:

```bash
Messaging__AuthMode=ManagedIdentity
Messaging__FullyQualifiedNamespace=your-ns.servicebus.windows.net
ConnectionStrings__AuditDb=Server=your-server.database.windows.net;Authentication=Active Directory Managed Identity;...
```

KEDA `ScaledObject` manifests for queue and topic autoscaling are in [`deploy/keda/`](deploy/keda/).
