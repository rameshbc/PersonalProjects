namespace Messaging.Core.Audit.Repositories;

using Messaging.Core.Abstractions;
using Messaging.Core.Audit.DbContext;
using Messaging.Core.Audit.Models;
using Messaging.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

internal sealed class EfCoreAuditRepository : IAuditRepository
{
    private readonly IDbContextFactory<MessagingAuditDbContext> _factory;
    private readonly ILogger<EfCoreAuditRepository> _logger;

    public EfCoreAuditRepository(
        IDbContextFactory<MessagingAuditDbContext> factory,
        ILogger<EfCoreAuditRepository> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    public async Task InsertAsync(MessageAuditLog entry, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.MessageAuditLogs.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(
        long id, MessageStatus status, string? statusDetail, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.MessageAuditLogs
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(x => x.Status,       status)
                     .SetProperty(x => x.StatusDetail, statusDetail)
                     .SetProperty(x => x.UpdatedAt,    DateTime.UtcNow),
                    ct);
    }

    public async Task<IReadOnlyList<MessageAuditLog>> QueryRecentAsync(
        string? destinationName = null,
        DateTime? since = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.MessageAuditLogs.AsNoTracking();
        if (destinationName is not null)
            query = query.Where(x => x.DestinationName == destinationName);
        if (since is not null)
            query = query.Where(x => x.CreatedAt >= since.Value);
        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> CountPendingAsync(
        string clientId,
        string destinationName,
        string? subject,
        DateTime cutoff,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        return subject is not null
            ? await CompiledQueries.PendingCountWithSubject(db, clientId, destinationName, subject, cutoff)
            : await CompiledQueries.PendingCountNoSubject(db, clientId, destinationName, cutoff);
    }
}
