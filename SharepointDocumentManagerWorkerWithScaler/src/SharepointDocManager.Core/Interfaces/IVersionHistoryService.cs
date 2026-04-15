using SharepointDocManager.Core.Entities;

namespace SharepointDocManager.Core.Interfaces;

/// <summary>
/// Provides access to document version history stored in SharePoint / SPE.
/// SharePoint maintains automatic version history for document libraries
/// when versioning is enabled on the library.
/// </summary>
public interface IVersionHistoryService
{
    /// <summary>
    /// Returns all versions of a document in descending order (newest first).
    /// </summary>
    Task<IReadOnlyList<DocumentVersion>> GetVersionsAsync(
        string clientId,
        string itemId,
        CancellationToken ct);

    /// <summary>
    /// Returns the download URL for a specific version.
    /// </summary>
    Task<string> GetVersionDownloadUrlAsync(
        string clientId,
        string itemId,
        string versionId,
        CancellationToken ct);

    /// <summary>
    /// Restores a document to a specific version.
    /// Creates a new version entry that matches the content of the specified historical version.
    /// </summary>
    Task RestoreVersionAsync(
        string clientId,
        string itemId,
        string versionId,
        CancellationToken ct);
}
