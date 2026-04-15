using SharepointDocManager.Core.Enums;

namespace SharepointDocManager.Core.Models;

/// <summary>
/// Declarative specification of the folder tree to provision inside DocLibrary-A.
/// Passed to FolderProvisioningService which walks it depth-first to create
/// folders and apply permissions idempotently.
///
/// Example usage:
///   var spec = new FolderStructureSpec
///   {
///       ClientId = "client-001",
///       RootName = "RootParent",
///       Children =
///       [
///           new FolderNode { Name = "Parent-A", Level = FolderLevel.Parent,
///               Children = [ new FolderNode { Name = "Child-A", Level = FolderLevel.Child } ] },
///           new FolderNode { Name = "Parent-C", Level = FolderLevel.Parent,
///               Children =
///               [
///                   new FolderNode { Name = "Child-A", Level = FolderLevel.Child },
///                   new FolderNode { Name = "Child-B", Level = FolderLevel.Child, IsProtected = true }
///               ]}
///       ]
///   };
/// </summary>
public sealed class FolderStructureSpec
{
    public string           ClientId  { get; init; } = string.Empty;
    public string           RootName  { get; init; } = "RootParent";
    public List<FolderNode> Children  { get; init; } = [];
}

/// <summary>
/// A single node in the folder tree specification.
/// IsProtected = true breaks permission inheritance and restricts to Admin role only.
/// </summary>
public sealed class FolderNode
{
    public string           Name        { get; init; } = string.Empty;
    public FolderLevel      Level       { get; init; }
    public bool             IsProtected { get; init; }
    public List<FolderNode> Children    { get; init; } = [];
}
