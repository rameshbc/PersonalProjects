namespace Messaging.Core.DI;

using Messaging.Core.Abstractions;
using Messaging.Core.Audit.Models;
using Messaging.Core.Models;

/// <summary>No-op audit repository used when audit is disabled in options.</summary>
internal sealed class NullAuditRepository : IAuditRepository
{
    public Task InsertAsync(MessageAuditLog entry, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateStatusAsync(long id, MessageStatus status, string? statusDetail, CancellationToken ct = default) => Task.CompletedTask;
    public Task<int> CountPendingAsync(string clientId, string destinationName, string? subject, DateTime cutoff, CancellationToken ct = default) => Task.FromResult(0);
    public Task<IReadOnlyList<MessageAuditLog>> QueryRecentAsync(string? destinationName = null, DateTime? since = null, int limit = 200, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<MessageAuditLog>>(Array.Empty<MessageAuditLog>());
}
