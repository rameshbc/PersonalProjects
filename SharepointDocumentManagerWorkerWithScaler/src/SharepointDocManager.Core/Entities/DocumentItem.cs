namespace SharepointDocManager.Core.Entities;

/// <summary>
/// Represents a file (document) within a client's DocLibrary-A.
/// Used in document list responses and as the unit of work for upload operations.
/// </summary>
public sealed class DocumentItem
{
    public string  DriveItemId       { get; set; } = string.Empty;
    public string  Name              { get; set; } = string.Empty;

    /// <summary>Relative path from DocLibrary-A root. e.g. "Parent-A/Child-A/Report.xlsx"</summary>
    public string  RelativePath      { get; set; } = string.Empty;

    public string  ParentFolderId    { get; set; } = string.Empty;
    public long    SizeBytes         { get; set; }
    public string? MimeType          { get; set; }
    public string? ETag              { get; set; }

    /// <summary>
    /// Pre-signed URL for opening the document in Office Online (browser-based edit).
    /// Retrieved via Graph /workbook or the driveItem webUrl.
    /// Short-lived — do not cache beyond the current request.
    /// </summary>
    public string? OnlineEditUrl     { get; set; }

    public string? CreatedBy         { get; set; }
    public string? LastModifiedBy    { get; set; }
    public DateTimeOffset CreatedAt      { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
}
