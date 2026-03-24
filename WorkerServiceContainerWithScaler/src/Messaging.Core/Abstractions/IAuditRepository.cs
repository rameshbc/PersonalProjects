#nullable enable

namespace Messaging.Core.Abstractions;

using Messaging.Core.Audit.Models;
using Messaging.Core.Models;

public interface IAuditRepository
{
    Task InsertAsync(MessageAuditLog entry, CancellationToken ct = default);
    Task UpdateStatusAsync(long id, MessageStatus status, string? statusDetail, CancellationToken ct = default);
    Task<int> CountPendingAsync(string clientId, string destinationName, string? subject, DateTime cutoff, CancellationToken ct = default);
}
