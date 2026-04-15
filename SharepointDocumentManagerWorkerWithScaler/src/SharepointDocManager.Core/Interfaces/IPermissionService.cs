using SharepointDocManager.Core.Entities;

namespace SharepointDocManager.Core.Interfaces;

/// <summary>
/// Manages Entra ID security groups and their folder-level assignments
/// within a client's document library.
///
/// Two implementations exist:
///   SharePointPermissionService  — for SP Online (uses Graph /sites/{id}/permissions
///                                  and drive item /invite endpoint)
///   SpePermissionService         — for SPE containers (uses container-level permission API)
///
/// The correct implementation is resolved via StorageAdapterResolver.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Ensures the three role groups ({ClientId}-Admin, -Contributor, -Reader) exist
    /// in Entra ID. Creates any that are missing. Idempotent.
    /// </summary>
    Task EnsureRoleGroupsAsync(string clientId, CancellationToken ct);

    /// <summary>
    /// Breaks permission inheritance on a folder then grants each group its role.
    /// Groups not in the provided list have their existing grants removed.
    /// Idempotent — calling twice with the same groups produces the same result.
    /// </summary>
    Task ApplyFolderPermissionsAsync(
        string clientId,
        string folderId,
        IEnumerable<PermissionGroup> groups,
        CancellationToken ct);

    /// <summary>
    /// Returns all current permission grants on a folder.
    /// Used by PermissionSyncWorker to detect drift.
    /// </summary>
    Task<IReadOnlyList<PermissionGroup>> GetFolderPermissionsAsync(
        string clientId,
        string folderId,
        CancellationToken ct);

    /// <summary>
    /// Removes all non-inherited permission grants from a folder and
    /// restores inheritance from the parent. Used during client offboarding.
    /// </summary>
    Task RemoveFolderPermissionsAsync(string clientId, string folderId, CancellationToken ct);
}
