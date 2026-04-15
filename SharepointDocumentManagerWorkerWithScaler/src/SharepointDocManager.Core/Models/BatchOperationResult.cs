using SharepointDocManager.Core.Entities;

namespace SharepointDocManager.Core.Models;

/// <summary>
/// Aggregated result of a batch upload or batch folder-create operation.
/// Returned by IDocumentStorageAdapter.BatchUploadAsync so callers can
/// inspect per-item outcomes without throwing on partial failure.
/// </summary>
public sealed class BatchOperationResult
{
    public int TotalRequested { get; init; }
    public int Succeeded      => Results.Count(r => r.Success);
    public int Failed         => Results.Count(r => !r.Success);
    public bool HasFailures   => Failed > 0;

    public List<ItemOperationResult> Results { get; init; } = [];
}

/// <summary>Outcome for a single item within a batch operation.</summary>
public sealed class ItemOperationResult
{
    /// <summary>File name or folder name that was the subject of this operation.</summary>
    public string        ItemName     { get; init; } = string.Empty;

    public bool          Success      { get; init; }

    /// <summary>The created or updated DocumentItem. Null when Success = false.</summary>
    public DocumentItem? Item         { get; init; }

    /// <summary>Error detail when Success = false.</summary>
    public string?       ErrorMessage { get; init; }

    /// <summary>Graph error code when the failure originated from Graph. e.g. "activityLimitReached"</summary>
    public string?       GraphErrorCode { get; init; }
}
