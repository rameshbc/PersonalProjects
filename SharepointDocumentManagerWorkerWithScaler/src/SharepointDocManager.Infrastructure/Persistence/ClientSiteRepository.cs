using Microsoft.EntityFrameworkCore;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Enums;
using SharepointDocManager.Core.Interfaces;
using SharepointDocManager.Infrastructure.Persistence.Entities;

namespace SharepointDocManager.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IClientSiteRepository backed by Azure SQL Database.
///
/// Uses a DbContext factory (IDbContextFactory) instead of a scoped DbContext
/// so it can be safely used from singleton services (adapters, workers) without
/// DbContext lifetime conflicts.
///
/// UpdateDeltaTokenAsync uses a targeted ExecuteUpdateAsync (EF 7+) — a single
/// UPDATE statement with no SELECT round-trip, safe for high-frequency writes
/// from PermissionSyncWorker.
/// </summary>
public sealed class ClientSiteRepository : IClientSiteRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ClientSiteRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<ClientSite?> GetByClientIdAsync(string clientId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.ClientSites
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientId == clientId, ct);
        return record is null ? null : MapToDomain(record);
    }

    public async Task<IReadOnlyList<ClientSite>> GetAllAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var records = await db.ClientSites.AsNoTracking().ToListAsync(ct);
        return records.Select(MapToDomain).ToList();
    }

    public async Task UpsertAsync(ClientSite site, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = MapToRecord(site);

        var existing = await db.ClientSites.FindAsync([record.ClientId], ct);
        if (existing is null)
        {
            db.ClientSites.Add(record);
        }
        else
        {
            existing.TenantId       = record.TenantId;
            existing.StorageBackend = record.StorageBackend;
            existing.SpSiteId       = record.SpSiteId;
            existing.SpDriveId      = record.SpDriveId;
            existing.SpeContainerId = record.SpeContainerId;
            existing.DeltaToken     = record.DeltaToken;
            existing.UpdatedAt      = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Targeted single-column UPDATE — avoids read-modify-write cycle.
    /// Called frequently by PermissionSyncWorker.
    /// </summary>
    public async Task UpdateDeltaTokenAsync(string clientId, string deltaToken, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.ClientSites
            .Where(x => x.ClientId == clientId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.DeltaToken, deltaToken)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static ClientSite MapToDomain(ClientSiteRecord r) => new()
    {
        ClientId       = r.ClientId,
        TenantId       = r.TenantId,
        StorageBackend = Enum.Parse<StorageBackend>(r.StorageBackend),
        SpSiteId       = r.SpSiteId,
        SpDriveId      = r.SpDriveId,
        SpeContainerId = r.SpeContainerId,
        DeltaToken     = r.DeltaToken,
        CreatedAt      = r.CreatedAt,
        UpdatedAt      = r.UpdatedAt
    };

    private static ClientSiteRecord MapToRecord(ClientSite s) => new()
    {
        ClientId       = s.ClientId,
        TenantId       = s.TenantId,
        StorageBackend = s.StorageBackend.ToString(),
        SpSiteId       = s.SpSiteId,
        SpDriveId      = s.SpDriveId,
        SpeContainerId = s.SpeContainerId,
        DeltaToken     = s.DeltaToken,
        CreatedAt      = s.CreatedAt,
        UpdatedAt      = s.UpdatedAt
    };
}
