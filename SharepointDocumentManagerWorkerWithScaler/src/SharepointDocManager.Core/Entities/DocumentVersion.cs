namespace SharepointDocManager.Core.Entities;

/// <summary>
/// Represents a single version entry in a document's version history.
/// Populated from Graph /drives/{id}/items/{id}/versions.
/// </summary>
public sealed class DocumentVersion
{
    /// <summary>Graph version ID. e.g. "1.0", "2.0", "3.0"</summary>
    public string          VersionId       { get; set; } = string.Empty;

    public string          DriveItemId     { get; set; } = string.Empty;

    /// <summary>Display label shown to users. e.g. "Version 3.0"</summary>
    public string?         VersionLabel    { get; set; }

    public long            SizeBytes       { get; set; }
    public string?         LastModifiedBy  { get; set; }
    public DateTimeOffset  LastModifiedAt  { get; set; }

    /// <summary>
    /// Download URL for this specific version.
    /// Requires the MI to have Files.ReadWrite.All.
    /// </summary>
    public string?         DownloadUrl     { get; set; }
}
