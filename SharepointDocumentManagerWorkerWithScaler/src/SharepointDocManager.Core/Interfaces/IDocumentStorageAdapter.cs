using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Core.Interfaces;

/// <summary>
/// The single abstraction over SharePoint Online and SharePoint Embedded storage.
///
/// Both adapters (SharePointAdapter, SharePointEmbeddedAdapter) implement this contract.
/// All application services call only this interface — they never branch on StorageBackend.
/// The StorageAdapterResolver in the Application layer selects the correct implementation
/// at runtime based on the client's configuration.
///
/// All methods are cancellation-token aware. Callers should pass a token sourced from
/// the HTTP request or the worker's CancellationToken so long-running operations
/// (large file uploads, batch operations) can be cleanly aborted.
/// </summary>
public interface IDocumentStorageAdapter
{
    // ── Folder operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a folder under the specified parent item.
    /// Idempotent — if a folder with the same name already exists, returns its existing ID.
    /// </summary>
    Task<string> CreateFolderAsync(string clientId, string parentItemId, string folderName, CancellationToken ct);

    /// <summary>
    /// Lists the immediate child folders of a given parent item.
    /// Does not recurse — callers enumerate depth-first if needed.
    /// </summary>
    Task<IReadOnlyList<DocumentFolder>> ListFoldersAsync(string clientId, string parentItemId, CancellationToken ct);

    // ── Document operations ───────────────────────────────────────────────────

    /// <summary>
    /// Uploads a single document. Internally selects single PUT vs resumable
    /// upload session based on UploadRequest.ContentLength vs 4 MB threshold.
    /// </summary>
    Task<DocumentItem> UploadDocumentAsync(UploadRequest request, CancellationToken ct);

    /// <summary>
    /// Uploads multiple documents concurrently using Graph $batch where possible.
    /// Respects MaxDegreeOfParallelism from the client's configuration.
    /// Never throws on partial failure — inspect BatchOperationResult.HasFailures.
    /// </summary>
    Task<BatchOperationResult> BatchUploadAsync(IEnumerable<UploadRequest> requests, CancellationToken ct);

    /// <summary>
    /// Returns all documents directly inside the specified folder (non-recursive).
    /// </summary>
    Task<IReadOnlyList<DocumentItem>> ListDocumentsAsync(string clientId, string folderId, CancellationToken ct);

    /// <summary>
    /// Returns the full version history for a document.
    /// </summary>
    Task<IReadOnlyList<DocumentVersion>> GetVersionHistoryAsync(string clientId, string itemId, CancellationToken ct);

    /// <summary>
    /// Returns the URL to open the document in Office Online for browser-based editing.
    /// For SP: uses the driveItem's webUrl with ?web=1.
    /// For SPE: uses the sharing link or embed URL.
    /// The URL is short-lived — do not cache.
    /// </summary>
    Task<string> GetOnlineEditUrlAsync(string clientId, string itemId, CancellationToken ct);

    // ── Permission operations ─────────────────────────────────────────────────

    /// <summary>
    /// Breaks permission inheritance on a folder and assigns the provided groups.
    /// Called by FolderProvisioningService after folder creation.
    /// Idempotent — existing grants for the same groups are not duplicated.
    /// </summary>
    Task SetFolderPermissionsAsync(string clientId, string folderId, IEnumerable<PermissionGroup> groups, CancellationToken ct);
}
