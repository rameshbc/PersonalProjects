using SharepointDocManager.Core.Entities;

namespace SharepointDocManager.Core.Interfaces;

/// <summary>
/// Persistence contract for ClientSite configuration records.
/// Implemented in the Infrastructure layer using EF Core.
/// </summary>
public interface IClientSiteRepository
{
    Task<ClientSite?>              GetByClientIdAsync(string clientId, CancellationToken ct);
    Task<IReadOnlyList<ClientSite>> GetAllAsync(CancellationToken ct);

    /// <summary>Inserts or updates (upsert) a ClientSite record.</summary>
    Task UpsertAsync(ClientSite site, CancellationToken ct);

    /// <summary>
    /// Updates only the DeltaToken field — called frequently by PermissionSyncWorker.
    /// Uses a targeted update to avoid overwriting other fields concurrently.
    /// </summary>
    Task UpdateDeltaTokenAsync(string clientId, string deltaToken, CancellationToken ct);
}
