using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Core.Interfaces;

/// <summary>
/// Application-level document operations. Orchestrates calls to IDocumentStorageAdapter
/// and emits audit events. Storage-backend agnostic.
/// </summary>
public interface IDocumentService
{
    /// <summary>Lists documents in the given folder for the specified client.</summary>
    Task<IReadOnlyList<DocumentItem>> ListAsync(string clientId, string folderId, CancellationToken ct);

    /// <summary>Uploads a single document and returns the created item.</summary>
    Task<DocumentItem> UploadAsync(UploadRequest request, CancellationToken ct);

    /// <summary>
    /// Uploads multiple documents. Progress events are pushed via SignalR.
    /// Never throws on partial failure — inspect BatchOperationResult.
    /// </summary>
    Task<BatchOperationResult> BatchUploadAsync(IEnumerable<UploadRequest> requests, CancellationToken ct);

    /// <summary>Returns the URL to open the document in Office Online.</summary>
    Task<string> GetOnlineEditUrlAsync(string clientId, string itemId, CancellationToken ct);
}
