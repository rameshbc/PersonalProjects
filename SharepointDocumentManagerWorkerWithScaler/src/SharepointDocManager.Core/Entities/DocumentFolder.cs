using SharepointDocManager.Core.Enums;

namespace SharepointDocManager.Core.Entities;

/// <summary>
/// Represents a folder within a client's DocLibrary-A.
/// Mirrors the Graph DriveItem (folder facet) — used for domain logic
/// such as permission provisioning decisions.
/// </summary>
public sealed class DocumentFolder
{
    public string      DriveItemId   { get; set; } = string.Empty;
    public string      Name          { get; set; } = string.Empty;

    /// <summary>Relative path from DocLibrary-A root. e.g. "Parent-A/Child-A"</summary>
    public string      RelativePath  { get; set; } = string.Empty;

    public string?     ParentItemId  { get; set; }
    public FolderLevel Level         { get; set; }

    /// <summary>
    /// When true, permission inheritance is broken and only Admin group is granted.
    /// Corresponds to "Child-B (Protected folder)" in the spec.
    /// </summary>
    public bool        IsProtected   { get; set; }

    public List<PermissionGroup> PermissionGroups { get; set; } = [];
}
