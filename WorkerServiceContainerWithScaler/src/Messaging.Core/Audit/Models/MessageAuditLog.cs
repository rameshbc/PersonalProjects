namespace Messaging.Core.Audit.Models;

using Messaging.Core.Models;

public sealed class MessageAuditLog
{
    public long Id { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public DestinationType DestinationType { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string? MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public string? Subject { get; set; }
    public byte[]? Body { get; set; }
    public bool IsBodyCompressed { get; set; }
    public int? BodySizeBytes { get; set; }
    public MessageStatus Status { get; set; }
    public string? StatusDetail { get; set; }
    public long? PendingCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
