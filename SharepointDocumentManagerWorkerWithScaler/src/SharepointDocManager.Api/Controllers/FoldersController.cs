using Microsoft.AspNetCore.Mvc;
using SharepointDocManager.Application.Commands;
using SharepointDocManager.Application.Handlers;
using SharepointDocManager.Application.Services;

namespace SharepointDocManager.Api.Controllers;

[ApiController]
[Route("api/clients/{clientId}/folders")]
public sealed class FoldersController : ControllerBase
{
    private readonly CreateFolderStructureHandler  _structureHandler;
    private readonly GrantFolderPermissionsHandler _permissionHandler;
    private readonly FolderProvisioningService     _folderService;

    public FoldersController(
        CreateFolderStructureHandler structureHandler,
        GrantFolderPermissionsHandler permissionHandler,
        FolderProvisioningService folderService)
    {
        _structureHandler  = structureHandler;
        _permissionHandler = permissionHandler;
        _folderService     = folderService;
    }

    /// <summary>GET api/clients/{clientId}/folders/{folderId}/children</summary>
    [HttpGet("{folderId}/children")]
    public async Task<IActionResult> ListChildren(string clientId, string folderId, CancellationToken ct)
    {
        var folders = await _folderService.ListFoldersAsync(clientId, folderId, ct);
        return Ok(folders);
    }

    /// <summary>
    /// POST api/clients/{clientId}/folders/structure
    /// Body: FolderStructureSpec — provisions the full DocLibrary-A tree.
    /// </summary>
    [HttpPost("structure")]
    public async Task<IActionResult> ProvisionStructure(
        string clientId,
        [FromBody] Core.Models.FolderStructureSpec spec,
        CancellationToken ct)
    {
        if (spec.ClientId != clientId)
            return BadRequest("clientId in route must match spec.ClientId.");

        await _structureHandler.HandleAsync(new CreateFolderStructureCommand(spec), ct);
        return NoContent();
    }

    /// <summary>
    /// PUT api/clients/{clientId}/folders/{folderId}/permissions
    /// Body: array of PermissionGroup — breaks inheritance and applies grants.
    /// </summary>
    [HttpPut("{folderId}/permissions")]
    public async Task<IActionResult> SetPermissions(
        string clientId, string folderId,
        [FromBody] Core.Entities.PermissionGroup[] groups,
        CancellationToken ct)
    {
        await _permissionHandler.HandleAsync(
            new GrantFolderPermissionsCommand(clientId, folderId, groups), ct);
        return NoContent();
    }
}
