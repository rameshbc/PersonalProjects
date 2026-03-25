#nullable enable

namespace Messaging.Core.Abstractions;

using Messaging.Core.Audit.Models;
using Messaging.Core.Models;

public interface IAuditRepository
{
    Task InsertAsync(MessageAuditLog entry, CancellationToken ct = default);
    Task UpdateStatusAsync(long id, MessageStatus status, string? statusDetail, CancellationToken ct = default);
    Task<int> CountPendingAsync(string clientId, string destinationName, string? subject, DateTime cutoff, CancellationToken ct = default);

    Task<IReadOnlyList<MessageAuditLog>> QueryRecentAsync(
        string? destinationName = null,
        DateTime? since = null,
        int limit = 200,
        CancellationToken ct = default);
}
