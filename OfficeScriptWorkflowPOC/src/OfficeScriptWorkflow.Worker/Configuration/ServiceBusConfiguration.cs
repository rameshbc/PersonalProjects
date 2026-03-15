namespace OfficeScriptWorkflow.Worker.Configuration;

/// <summary>
/// Azure Service Bus configuration for the distributed operation queue.
///
/// When UseServiceBus = false (default) the worker uses an in-memory Channel queue,
/// suitable for a single-replica deployment.
///
/// When UseServiceBus = true the worker reads from Azure Service Bus using
/// session-enabled queues. SessionId = WorkbookId, which guarantees that all
/// operations targeting the same workbook are processed by exactly one replica
/// at a time — preventing concurrent write collisions in Excel.
/// </summary>
public class ServiceBusConfiguration
{
    public bool UseServiceBus { get; set; } = false;

    /// <summary>
    /// Fully qualified Service Bus namespace connection string.
    /// Store in Azure Key Vault; reference via Key Vault reference in app config.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Name of the session-enabled queue. Must be created with sessions enabled
    /// in Azure — this cannot be changed after creation.
    /// </summary>
    public string QueueName { get; set; } = "excel-operations";

    /// <summary>
    /// Number of workbook sessions this replica holds simultaneously.
    /// Keep at 1 — one replica processes one workbook at a time, sequentially.
    /// Parallel workbook processing is achieved by running more replicas,
    /// each acquiring a different workbook session from Service Bus.
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 1;

    /// <summary>
    /// Message lock duration in seconds. Must exceed the Office Script max runtime (300s).
    /// Configure this on the Service Bus queue resource, not just here — this value
    /// is used only for documentation/validation reference.
    /// </summary>
    public int MessageLockDurationSeconds { get; set; } = 360;
}
