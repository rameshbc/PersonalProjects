using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Core.Interfaces;

/// <summary>
/// Folder lifecycle operations. Delegates to IDocumentStorageAdapter but adds
/// domain rules such as idempotency, depth-first tree creation, and audit events.
/// </summary>
public interface IFolderService
{
    /// <summary>
    /// Provisions the complete folder tree described by the spec.
    /// Walks nodes depth-first — parent always created before children.
    /// Idempotent: existing folders at any level are detected and skipped.
    /// </summary>
    Task ProvisionStructureAsync(FolderStructureSpec spec, CancellationToken ct);

    /// <summary>Creates a single folder and returns its drive item ID.</summary>
    Task<string> CreateFolderAsync(string clientId, string parentItemId, string folderName, CancellationToken ct);

    /// <summary>Lists immediate child folders of a parent item.</summary>
    Task<IReadOnlyList<DocumentFolder>> ListFoldersAsync(string clientId, string parentItemId, CancellationToken ct);
}
