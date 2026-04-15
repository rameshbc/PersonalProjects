namespace SharepointDocManager.Core.Enums;

/// <summary>
/// Application-level roles that map to SharePoint/SPE permission levels on subfolders.
///
/// Mapping:
///   Admin       → SP "Full Control" / SPE "owner"
///   Contributor → SP "Contribute"   / SPE "write"
///   Reader      → SP "Read"         / SPE "read"
///
/// Role groups are named {ClientId}-{Role} in Entra ID, e.g. "client-001-Contributor".
/// Group membership is managed externally (IdP/HR sync).
/// The application only manages folder-level assignment of these groups.
/// </summary>
public enum DocumentRole
{
    Admin,
    Contributor,
    Reader
}
