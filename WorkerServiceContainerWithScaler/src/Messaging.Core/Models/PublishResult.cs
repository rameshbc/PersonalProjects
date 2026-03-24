#nullable enable

namespace Messaging.Core.Models;

public sealed record PublishResult(
    PublishStatus Status,
    string? MessageId,
    long? PendingCount,
    string? SuppressReason,
    Exception? Exception)
{
    public static PublishResult Success(string messageId) =>
        new(PublishStatus.Published, messageId, null, null, null);

    public static PublishResult Suppressed(string messageId, long pendingCount, string reason) =>
        new(PublishStatus.Suppressed, messageId, pendingCount, reason, null);

    public static PublishResult Failed(string messageId, Exception ex) =>
        new(PublishStatus.PublishFailed, messageId, null, null, ex);
}
