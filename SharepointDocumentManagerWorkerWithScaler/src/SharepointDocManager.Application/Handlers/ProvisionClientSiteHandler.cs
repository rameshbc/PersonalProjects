using Microsoft.Extensions.Logging;
using SharepointDocManager.Application.Commands;
using SharepointDocManager.Application.Services;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Interfaces;

namespace SharepointDocManager.Application.Handlers;

/// <summary>
/// Handles ProvisionClientSiteCommand.
///
/// Steps:
///   1. Persist ClientSite config (upsert) so StorageAdapterResolver can resolve the adapter.
///   2. Ensure Entra role groups exist ({ClientId}-Admin, -Contributor, -Reader).
///   3. Provision the complete DocLibrary-A folder tree with permissions.
///
/// Idempotent — safe to re-run if provisioning was interrupted mid-way.
/// </summary>
public sealed class ProvisionClientSiteHandler
{
    private readonly IClientSiteRepository     _siteRepo;
    private readonly StorageAdapterResolver    _resolver;
    private readonly FolderProvisioningService _folderService;
    private readonly ILogger<ProvisionClientSiteHandler> _logger;

    public ProvisionClientSiteHandler(
        IClientSiteRepository siteRepo,
        StorageAdapterResolver resolver,
        FolderProvisioningService folderService,
        ILogger<ProvisionClientSiteHandler> logger)
    {
        _siteRepo      = siteRepo;
        _resolver      = resolver;
        _folderService = folderService;
        _logger        = logger;
    }

    public async Task HandleAsync(ProvisionClientSiteCommand command, CancellationToken ct)
    {
        _logger.LogInformation(
            "[Provision] Starting provisioning for client '{Client}' on {Backend}.",
            command.ClientId, command.StorageBackend);

        // Step 1: Upsert ClientSite so the adapter resolver works immediately
        var site = new ClientSite
        {
            ClientId       = command.ClientId,
            TenantId       = command.TenantId,
            StorageBackend = command.StorageBackend,
            SpSiteId       = string.Empty,       // Resolved by adapter from SpSiteUrl if needed
            SpDriveId      = string.Empty,
            SpeContainerId = command.SpeContainerId,
            CreatedAt      = DateTimeOffset.UtcNow,
            UpdatedAt      = DateTimeOffset.UtcNow
        };
        await _siteRepo.UpsertAsync(site, ct);

        // Step 2: Ensure Entra role groups exist
        var permService = await _resolver.ResolvePermissionServiceAsync(command.ClientId, ct);
        await permService.EnsureRoleGroupsAsync(command.ClientId, ct);

        // Step 3: Provision folder tree
        await _folderService.ProvisionStructureAsync(command.FolderSpec, ct);

        _logger.LogInformation(
            "[Provision] Provisioning complete for client '{Client}'.", command.ClientId);
    }
}
