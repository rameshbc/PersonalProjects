using SharepointDocManager.Core.Enums;

namespace SharepointDocManager.Core.Entities;

/// <summary>
/// Represents an Entra ID security group that holds a role on a client's folder.
///
/// Naming convention: {ClientId}-{Role}
///   e.g. "client-001-Admin", "client-001-Contributor", "client-001-Reader"
///
/// Group membership is managed externally (IdP/HR sync).
/// This entity only captures the group's identity and its folder role.
/// </summary>
public sealed class PermissionGroup
{
    /// <summary>Entra ID object ID (GUID) of the security group.</summary>
    public string       GroupId     { get; set; } = string.Empty;

    /// <summary>Display name. e.g. "client-001-Contributor"</summary>
    public string       DisplayName { get; set; } = string.Empty;

    public DocumentRole Role        { get; set; }

    /// <summary>
    /// The Graph permission ID returned after granting access to a drive item.
    /// Stored so permissions can be updated or revoked without re-querying Graph.
    /// </summary>
    public string?      PermissionId { get; set; }
}
