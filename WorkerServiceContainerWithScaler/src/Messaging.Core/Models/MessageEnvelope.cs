#nullable enable

namespace Messaging.Core.Models;

public sealed class MessageEnvelope
{
    /// <summary>
    /// Identity of the publishing client. Set by caller per-message.
    /// Stamped as Service Bus application property "x-messaging-client-id".
    /// Used as the primary key for the pending-message check.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }

    /// <summary>Message-type discriminator. Also used in the pending check key.</summary>
    public string? Subject { get; init; }

    /// <summary>"application/json" or "application/json+gzip" when compressed.</summary>
    public string? ContentType { get; init; }

    public bool IsCompressed { get; init; }
    public ReadOnlyMemory<byte> Body { get; init; }
    public IReadOnlyDictionary<string, object> ApplicationProperties { get; init; }
        = new Dictionary<string, object>();
    public DateTimeOffset? ScheduledEnqueueTime { get; init; }
    public string? SessionId { get; init; }
}
