namespace SharepointDocManager.Infrastructure.Persistence.Entities;

/// <summary>
/// Append-only audit log entry. All admin actions and significant document
/// operations are recorded here for compliance and debugging.
/// </summary>
public sealed class AuditLogRecord
{
    public long   Id         { get; set; }
    public string ActorId    { get; set; } = string.Empty;   // User or system principal
    public string Action     { get; set; } = string.Empty;   // e.g. FolderCreated, PermissionGranted
    public string ClientId   { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;   // Drive item ID, site ID, etc.
    public string? Details   { get; set; }                   // JSON — action-specific metadata
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
