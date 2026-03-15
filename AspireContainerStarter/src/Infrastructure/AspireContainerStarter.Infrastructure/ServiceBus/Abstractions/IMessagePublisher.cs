namespace AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;

/// <summary>
/// Publishes messages to an Azure Service Bus queue or topic.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Sends a single message to the configured queue/topic.
    /// </summary>
    Task PublishAsync<T>(T message, string? correlationId = null, CancellationToken ct = default)
        where T : class;

    /// <summary>
    /// Sends a batch of messages to the configured queue/topic.
    /// </summary>
    Task PublishBatchAsync<T>(IEnumerable<T> messages, CancellationToken ct = default)
        where T : class;
}
