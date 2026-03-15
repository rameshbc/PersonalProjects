namespace AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;

/// <summary>
/// Processes messages received from an Azure Service Bus queue.
/// Implement this interface in each worker service to define per-message logic.
/// </summary>
public interface IMessageConsumer<T> where T : class
{
    /// <summary>
    /// Called for each message received from the queue.
    /// Throw to trigger dead-lettering after max delivery count is exceeded.
    /// </summary>
    Task HandleAsync(T message, string messageId, string? correlationId, CancellationToken ct);
}
