namespace SharepointDocManager.Core.Models;

/// <summary>
/// Describes a single file upload operation passed from the application layer
/// to the IDocumentStorageAdapter. The adapter decides whether to use a single
/// PUT or a resumable upload session based on ContentLength vs the 4 MB threshold.
/// </summary>
public sealed class UploadRequest
{
    /// <summary>Target folder's Graph drive item ID.</summary>
    public string ClientId       { get; init; } = string.Empty;

    public string ParentFolderId { get; init; } = string.Empty;

    /// <summary>File name with extension. e.g. "Q1-Report.xlsx"</summary>
    public string FileName       { get; init; } = string.Empty;

    /// <summary>
    /// Readable stream of file content. The caller is responsible for disposal.
    /// The adapter will not buffer the entire stream into memory for large files —
    /// it reads it in chunks directly into the upload session.
    /// </summary>
    public Stream Content        { get; init; } = Stream.Null;

    public long   ContentLength  { get; init; }
    public string ContentType    { get; init; } = "application/octet-stream";

    /// <summary>
    /// What to do if a file with the same name already exists.
    /// Defaults to Replace to support re-upload / refresh scenarios.
    /// </summary>
    public ConflictBehaviour ConflictBehaviour { get; init; } = ConflictBehaviour.Replace;
}

public enum ConflictBehaviour { Replace, Rename, Fail }
