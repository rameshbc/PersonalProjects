using SharepointDocManager.Application.Commands;
using SharepointDocManager.Application.Services;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Application.Handlers;

/// <summary>Handles UploadDocumentCommand — single file upload.</summary>
public sealed class UploadDocumentHandler
{
    private readonly DocumentOrchestrationService _orchestration;

    public UploadDocumentHandler(DocumentOrchestrationService orchestration)
        => _orchestration = orchestration;

    public Task<DocumentItem> HandleAsync(UploadDocumentCommand command, CancellationToken ct)
        => _orchestration.UploadAsync(command.Request, ct);
}

/// <summary>
/// Handles BatchUploadDocumentsCommand — parallel multi-file upload via Channel.
/// Pass an onProgress callback to wire progress events to SignalR in the API layer.
/// </summary>
public sealed class BatchUploadDocumentsHandler
{
    private readonly DocumentOrchestrationService _orchestration;

    public BatchUploadDocumentsHandler(DocumentOrchestrationService orchestration)
        => _orchestration = orchestration;

    public Task<BatchOperationResult> HandleAsync(
        BatchUploadDocumentsCommand command,
        Action<UploadProgressEvent>? onProgress,
        CancellationToken ct)
        => _orchestration.BatchUploadWithProgressAsync(command.Requests, onProgress, ct);
}
