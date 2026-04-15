namespace SharepointDocManager.Infrastructure.Persistence.Entities;

/// <summary>
/// EF Core persistence record for client site configuration.
/// Separate from the Core.Entities.ClientSite domain entity to keep EF concerns
/// out of the domain layer. The repository maps between the two.
/// </summary>
public sealed class ClientSiteRecord
{
    public string  ClientId       { get; set; } = string.Empty;
    public string  TenantId       { get; set; } = string.Empty;
    public string  StorageBackend { get; set; } = "SharePointOnline";
    public string  SpSiteId       { get; set; } = string.Empty;
    public string  SpDriveId      { get; set; } = string.Empty;
    public string  SpeContainerId { get; set; } = string.Empty;
    public string? DeltaToken     { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
