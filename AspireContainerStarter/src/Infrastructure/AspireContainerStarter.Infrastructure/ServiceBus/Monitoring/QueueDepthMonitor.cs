using Azure.Messaging.ServiceBus.Administration;

namespace AspireContainerStarter.Infrastructure.ServiceBus.Monitoring;

/// <summary>
/// Queries the active message count for a Service Bus queue using
/// <see cref="ServiceBusAdministrationClient"/> (Managed Identity auth).
/// </summary>
public sealed class QueueDepthMonitor
{
    private readonly ServiceBusAdministrationClient _adminClient;

    public QueueDepthMonitor(ServiceBusAdministrationClient adminClient)
    {
        _adminClient = adminClient;
    }

    /// <summary>
    /// Returns the number of active (non-dead-lettered) messages waiting
    /// in the specified queue.
    /// </summary>
    public async Task<long> GetActiveMessageCountAsync(string queueName, CancellationToken ct = default)
    {
        var response = await _adminClient.GetQueueRuntimePropertiesAsync(queueName, ct);
        return response.Value.ActiveMessageCount;
    }
}
