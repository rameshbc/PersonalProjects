using AspireContainerStarter.Contracts.Enums;

namespace AspireContainerStarter.Contracts.Messages;

/// <summary>
/// Published to the "job-progress" Service Bus topic by workers
/// as a job moves through its lifecycle. The API subscribes and
/// forwards updates to connected clients via SignalR.
/// </summary>
public sealed record JobProgressMessage(
    Guid          JobId,
    string        JobType,       // "Fed" | "State"
    JobStatus     Status,
    int           PercentComplete,
    string?       StatusDetail,
    DateTimeOffset UpdatedAt);
