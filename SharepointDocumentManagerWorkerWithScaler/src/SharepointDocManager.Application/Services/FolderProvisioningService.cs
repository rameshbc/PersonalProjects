using Microsoft.Extensions.Logging;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Enums;
using SharepointDocManager.Core.Interfaces;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Application.Services;

/// <summary>
/// Provisions the complete folder tree for a client's DocLibrary-A.
///
/// Walks a FolderStructureSpec depth-first (parent before children) and:
///   1. Creates each folder via IDocumentStorageAdapter (idempotent — 409 = skip).
///   2. For Parent-level folders: breaks inheritance and applies role group grants.
///   3. For protected Child folders (IsProtected = true): Admin-only grants.
///   4. For non-protected Child folders: inherits from parent (no extra grants).
///
/// This is called:
///   • During initial site provisioning (Admin portal).
///   • When a new parent or child folder is added to an existing client structure.
///
/// All operations are idempotent — safe to re-run on an existing library.
/// </summary>
public sealed class FolderProvisioningService : IFolderService
{
    private readonly StorageAdapterResolver  _resolver;
    private readonly IClientSiteRepository   _siteRepo;
    private readonly ILogger<FolderProvisioningService> _logger;

    public FolderProvisioningService(
        StorageAdapterResolver resolver,
        IClientSiteRepository siteRepo,
        ILogger<FolderProvisioningService> logger)
    {
        _resolver = resolver;
        _siteRepo = siteRepo;
        _logger   = logger;
    }

    // ── IFolderService ────────────────────────────────────────────────────────

    public async Task ProvisionStructureAsync(FolderStructureSpec spec, CancellationToken ct)
    {
        _logger.LogInformation(
            "[FolderProvisioning] Starting structure provisioning for client '{Client}'.", spec.ClientId);

        var adapter         = await _resolver.ResolveAsync(spec.ClientId, ct);
        var permService     = await _resolver.ResolvePermissionServiceAsync(spec.ClientId, ct);
        var roleGroups      = await BuildRoleGroupsAsync(spec.ClientId, permService, ct);

        // Create the root folder first
        var rootId = await adapter.CreateFolderAsync(spec.ClientId, "root", spec.RootName, ct);

        // Walk child nodes depth-first
        foreach (var parentNode in spec.Children)
        {
            await ProvisionNodeAsync(
                spec.ClientId, adapter, permService, roleGroups,
                parentNode, rootId, ct);
        }

        _logger.LogInformation(
            "[FolderProvisioning] Completed structure provisioning for client '{Client}'.", spec.ClientId);
    }

    public async Task<string> CreateFolderAsync(
        string clientId, string parentItemId, string folderName, CancellationToken ct)
    {
        var adapter = await _resolver.ResolveAsync(clientId, ct);
        return await adapter.CreateFolderAsync(clientId, parentItemId, folderName, ct);
    }

    public async Task<IReadOnlyList<Core.Entities.DocumentFolder>> ListFoldersAsync(
        string clientId, string parentItemId, CancellationToken ct)
    {
        var adapter = await _resolver.ResolveAsync(clientId, ct);
        return await adapter.ListFoldersAsync(clientId, parentItemId, ct);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task ProvisionNodeAsync(
        string clientId,
        IDocumentStorageAdapter adapter,
        IPermissionService permService,
        Dictionary<DocumentRole, PermissionGroup> roleGroups,
        FolderNode node,
        string parentItemId,
        CancellationToken ct)
    {
        // Create the folder (idempotent)
        var folderId = await adapter.CreateFolderAsync(clientId, parentItemId, node.Name, ct);

        // Apply permissions based on folder level and protection flag
        if (node.Level == FolderLevel.Parent)
        {
            // Parent folders: break inheritance, grant all three role groups
            var groups = roleGroups.Values.ToList();
            await permService.ApplyFolderPermissionsAsync(clientId, folderId, groups, ct);

            _logger.LogInformation(
                "[FolderProvisioning] Applied role groups to Parent '{Name}' (client {Client})",
                node.Name, clientId);
        }
        else if (node.Level == FolderLevel.Child && node.IsProtected)
        {
            // Protected child (e.g. Child-B): Admin only
            var adminOnly = new[] { roleGroups[DocumentRole.Admin] };
            await permService.ApplyFolderPermissionsAsync(clientId, folderId, adminOnly, ct);

            _logger.LogInformation(
                "[FolderProvisioning] Applied Admin-only grants to protected Child '{Name}' (client {Client})",
                node.Name, clientId);
        }
        // Non-protected children: no explicit grants — inherit from parent

        // Recurse into children
        foreach (var child in node.Children)
        {
            await ProvisionNodeAsync(clientId, adapter, permService, roleGroups, child, folderId, ct);
        }
    }

    /// <summary>
    /// Loads the three role groups from Entra (creating them if missing)
    /// and returns them keyed by DocumentRole for quick lookup.
    /// </summary>
    private async Task<Dictionary<DocumentRole, PermissionGroup>> BuildRoleGroupsAsync(
        string clientId, IPermissionService permService, CancellationToken ct)
    {
        await permService.EnsureRoleGroupsAsync(clientId, ct);

        // Load all groups for the client; map by role
        var result = new Dictionary<DocumentRole, PermissionGroup>();
        foreach (var role in Enum.GetValues<DocumentRole>())
        {
            result[role] = new PermissionGroup
            {
                DisplayName = $"{clientId}-{role}",
                Role        = role
                // GroupId is resolved at the permService.ApplyFolderPermissionsAsync level
                // via a Group lookup — PermissionService owns the Entra Group API calls
            };
        }
        return result;
    }
}
