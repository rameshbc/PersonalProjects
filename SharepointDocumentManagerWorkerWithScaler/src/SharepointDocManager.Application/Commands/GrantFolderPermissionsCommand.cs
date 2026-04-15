using SharepointDocManager.Core.Entities;

namespace SharepointDocManager.Application.Commands;

/// <summary>
/// Breaks inheritance on a folder and applies explicit role-group grants.
/// </summary>
public sealed record GrantFolderPermissionsCommand(
    string                     ClientId,
    string                     FolderId,
    IReadOnlyList<PermissionGroup> Groups);
