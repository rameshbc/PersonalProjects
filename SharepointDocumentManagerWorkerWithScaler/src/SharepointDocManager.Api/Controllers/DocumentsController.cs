using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SharepointDocManager.Application.Commands;
using SharepointDocManager.Application.Handlers;
using SharepointDocManager.Application.Queries;
using SharepointDocManager.Application.Services;
using SharepointDocManager.Api.Hubs;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Api.Controllers;

/// <summary>
/// Document CRUD and batch upload endpoints.
///
/// All endpoints require the X-Client-Id header (set by ClientContextMiddleware).
/// ClientId scopes the operation to the correct SP or SPE library automatically —
/// callers never specify the storage backend.
/// </summary>
[ApiController]
[Route("api/clients/{clientId}/folders/{folderId}/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly GetDocumentListHandler       _listHandler;
    private readonly UploadDocumentHandler        _uploadHandler;
    private readonly BatchUploadDocumentsHandler  _batchUploadHandler;
    private readonly IHubContext<UploadProgressHub> _hub;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        GetDocumentListHandler listHandler,
        UploadDocumentHandler uploadHandler,
        BatchUploadDocumentsHandler batchUploadHandler,
        IHubContext<UploadProgressHub> hub,
        ILogger<DocumentsController> logger)
    {
        _listHandler       = listHandler;
        _uploadHandler     = uploadHandler;
        _batchUploadHandler = batchUploadHandler;
        _hub               = hub;
        _logger            = logger;
    }

    /// <summary>GET api/clients/{clientId}/folders/{folderId}/documents</summary>
    [HttpGet]
    public async Task<IActionResult> List(string clientId, string folderId, CancellationToken ct)
    {
        var items = await _listHandler.HandleAsync(new GetDocumentListQuery(clientId, folderId), ct);
        return Ok(items);
    }

    /// <summary>POST api/clients/{clientId}/folders/{folderId}/documents — single file upload</summary>
    [HttpPost]
    [RequestSizeLimit(250 * 1024 * 1024)]  // 250 MB max single file
    public async Task<IActionResult> Upload(
        string clientId, string folderId,
        IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            return BadRequest("File is empty.");

        await using var stream = file.OpenReadStream();

        var item = await _uploadHandler.HandleAsync(new UploadDocumentCommand(new UploadRequest
        {
            ClientId       = clientId,
            ParentFolderId = folderId,
            FileName       = file.FileName,
            Content        = stream,
            ContentLength  = file.Length,
            ContentType    = file.ContentType
        }), ct);

        return CreatedAtAction(nameof(List), new { clientId, folderId }, item);
    }

    /// <summary>
    /// POST api/clients/{clientId}/folders/{folderId}/documents/batch — multi-file upload.
    /// Progress events are pushed to SignalR group "{clientId}-uploads".
    /// </summary>
    [HttpPost("batch")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]  // 2 GB batch limit
    public async Task<IActionResult> BatchUpload(
        string clientId, string folderId,
        IFormFileCollection files, CancellationToken ct)
    {
        if (!files.Any())
            return BadRequest("No files provided.");

        var requests = files.Select(f => new UploadRequest
        {
            ClientId       = clientId,
            ParentFolderId = folderId,
            FileName       = f.FileName,
            Content        = f.OpenReadStream(),
            ContentLength  = f.Length,
            ContentType    = f.ContentType
        }).ToList();

        // Wire SignalR progress push — broadcast to the client's upload group
        void OnProgress(UploadProgressEvent evt)
        {
            _ = _hub.Clients.Group($"{clientId}-uploads").SendAsync(
                "uploadProgress",
                new { evt.FileName, evt.Completed, evt.Total, evt.Success },
                CancellationToken.None);
        }

        var result = await _batchUploadHandler.HandleAsync(
            new BatchUploadDocumentsCommand(clientId, requests),
            OnProgress, ct);

        // Dispose all streams
        foreach (var f in files)
            await f.OpenReadStream().DisposeAsync();

        return result.HasFailures
            ? StatusCode(207, result)   // 207 Multi-Status — partial success
            : Ok(result);
    }
}
