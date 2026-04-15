using Microsoft.AspNetCore.Mvc;
using SharepointDocManager.Application.Handlers;
using SharepointDocManager.Application.Queries;

namespace SharepointDocManager.Api.Controllers;

[ApiController]
[Route("api/clients/{clientId}/documents/{itemId}/versions")]
public sealed class VersionHistoryController : ControllerBase
{
    private readonly GetVersionHistoryHandler  _versionHandler;
    private readonly GetOnlineEditUrlHandler   _editUrlHandler;

    public VersionHistoryController(
        GetVersionHistoryHandler versionHandler,
        GetOnlineEditUrlHandler editUrlHandler)
    {
        _versionHandler = versionHandler;
        _editUrlHandler = editUrlHandler;
    }

    /// <summary>GET api/clients/{clientId}/documents/{itemId}/versions</summary>
    [HttpGet]
    public async Task<IActionResult> GetVersionHistory(
        string clientId, string itemId, CancellationToken ct)
    {
        var versions = await _versionHandler.HandleAsync(
            new GetVersionHistoryQuery(clientId, itemId), ct);
        return Ok(versions);
    }

    /// <summary>
    /// GET api/clients/{clientId}/documents/{itemId}/edit-url
    /// Returns the Office Online edit URL. Short-lived — do not cache client-side.
    /// </summary>
    [HttpGet("/api/clients/{clientId}/documents/{itemId}/edit-url")]
    public async Task<IActionResult> GetEditUrl(
        string clientId, string itemId, CancellationToken ct)
    {
        var url = await _editUrlHandler.HandleAsync(
            new GetOnlineEditUrlQuery(clientId, itemId), ct);
        return Ok(new { editUrl = url });
    }
}
