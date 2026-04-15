using SharepointDocManager.Application.Commands;
using SharepointDocManager.Application.Services;

namespace SharepointDocManager.Application.Handlers;

/// <summary>Handles CreateFolderStructureCommand — provisions the full DocLibrary-A folder tree.</summary>
public sealed class CreateFolderStructureHandler
{
    private readonly FolderProvisioningService _provisioningService;

    public CreateFolderStructureHandler(FolderProvisioningService provisioningService)
        => _provisioningService = provisioningService;

    public Task HandleAsync(CreateFolderStructureCommand command, CancellationToken ct)
        => _provisioningService.ProvisionStructureAsync(command.Spec, ct);
}
