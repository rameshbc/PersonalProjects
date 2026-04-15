namespace SharepointDocManager.Core.Enums;

/// <summary>
/// Represents the depth of a folder within the DocLibrary-A structure.
///
/// Structure:
///   Root        → DocLibrary-A root (RootParent) — no custom permission grants
///   Parent      → Parent-A, Parent-B, Parent-C   — permission inheritance is broken here
///   Child       → Child-A, Child-B               — inherits from Parent by default
///                                                   unless marked as Protected
///
/// The FolderLevel is used by FolderProvisioningService to decide whether to
/// break permission inheritance and apply role group grants.
/// </summary>
public enum FolderLevel
{
    Root,
    Parent,
    Child
}
