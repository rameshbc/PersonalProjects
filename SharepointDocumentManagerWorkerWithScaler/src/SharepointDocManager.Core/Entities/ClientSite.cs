using SharepointDocManager.Core.Enums;

namespace SharepointDocManager.Core.Entities;

/// <summary>
/// Represents a client's SharePoint or SPE storage configuration.
/// One record per client, stored in the application database.
///
/// SiteId/DriveId are SP-specific; ContainerId is SPE-specific.
/// Only the fields relevant to the active StorageBackend need to be populated.
/// </summary>
public sealed class ClientSite
{
    /// <summary>Stable, human-readable client identifier. Used as the partition key.</summary>
    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Determines which IDocumentStorageAdapter is used at runtime.</summary>
    public StorageBackend StorageBackend { get; set; } = StorageBackend.SharePointOnline;

    // ── SharePoint Online fields ──────────────────────────────────────────────

    /// <summary>Graph site ID. Format: {hostname},{siteId},{webId}</summary>
    public string SpSiteId  { get; set; } = string.Empty;

    /// <summary>Drive ID of the DocLibrary-A document library.</summary>
    public string SpDriveId { get; set; } = string.Empty;

    // ── SharePoint Embedded fields ────────────────────────────────────────────

    /// <summary>SPE container drive ID. Equivalent to DriveId in Graph calls.</summary>
    public string SpeContainerId { get; set; } = string.Empty;

    // ── Change tracking ───────────────────────────────────────────────────────

    /// <summary>
    /// Last Graph delta token for DocLibrary-A. Used by PermissionSyncWorker
    /// to fetch only changes since the previous sync — avoids full tree scans.
    /// </summary>
    public string? DeltaToken { get; set; }

    public DateTimeOffset CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt  { get; set; } = DateTimeOffset.UtcNow;
}
