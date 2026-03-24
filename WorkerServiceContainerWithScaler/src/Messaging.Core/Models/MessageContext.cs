#nullable enable

namespace Messaging.Core.Models;

using Azure.Messaging.ServiceBus;

public sealed class MessageContext
{
    public required string MessageId { get; init; }
    public string? CorrelationId { get; init; }
    public string? ClientId { get; init; }

    /// <summary>Queue name or "topic/subscription" composite.</summary>
    public required string DestinationName { get; init; }

    public required ServiceBusReceivedMessage RawMessage { get; init; }
    public required Func<CancellationToken, Task> CompleteAsync { get; init; }
    public required Func<string, string, CancellationToken, Task> DeadLetterAsync { get; init; }
    public required Func<TimeSpan?, CancellationToken, Task> AbandonAsync { get; init; }
    public int DeliveryCount => RawMessage.DeliveryCount;
}
