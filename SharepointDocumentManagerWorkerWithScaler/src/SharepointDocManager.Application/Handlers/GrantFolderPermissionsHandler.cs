using SharepointDocManager.Application.Commands;
using SharepointDocManager.Application.Services;

namespace SharepointDocManager.Application.Handlers;

/// <summary>Handles GrantFolderPermissionsCommand — breaks inheritance and applies role groups.</summary>
public sealed class GrantFolderPermissionsHandler
{
    private readonly StorageAdapterResolver _resolver;

    public GrantFolderPermissionsHandler(StorageAdapterResolver resolver) => _resolver = resolver;

    public async Task HandleAsync(GrantFolderPermissionsCommand command, CancellationToken ct)
    {
        var permService = await _resolver.ResolvePermissionServiceAsync(command.ClientId, ct);
        await permService.ApplyFolderPermissionsAsync(command.ClientId, command.FolderId, command.Groups, ct);
    }
}
