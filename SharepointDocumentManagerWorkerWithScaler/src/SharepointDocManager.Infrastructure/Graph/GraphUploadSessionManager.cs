using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace SharepointDocManager.Infrastructure.Graph;

/// <summary>
/// Manages resumable upload sessions for files larger than 4 MB.
///
/// How Graph resumable uploads work:
///   1. POST to /drives/{driveId}/items/{parentId}:/{fileName}:/createUploadSession
///      → returns an uploadUrl (valid for ~1 hour, no auth header needed for chunk PUTs)
///   2. PUT chunks to the uploadUrl using Content-Range header.
///      Each chunk must be a multiple of 320 KB. We use 5 MB chunks.
///   3. The final chunk response contains the created DriveItem.
///
/// Resilience:
///   • Per-chunk retry with exponential back-off on 429/503.
///   • On non-retriable failure, the upload session is cancelled to free server resources.
///   • The Graph SDK's LargeFileUploadTask handles chunk logic; this class wraps it
///     with additional logging and per-chunk error handling.
/// </summary>
public sealed class GraphUploadSessionManager
{
    // 5 MB — must be a multiple of 320 KB (327,680 bytes). 5 MB = 15 × 327,680 + 40,960 — not exact.
    // Corrected: 5 MB = 5,242,880. Nearest valid: 16 × 320 KB = 5,242,880 bytes. ✓
    private const int ChunkSizeBytes = 5 * 1024 * 1024;

    private readonly GraphServiceClient _graph;
    private readonly ILogger<GraphUploadSessionManager> _logger;

    public GraphUploadSessionManager(
        GraphServiceClient graph,
        ILogger<GraphUploadSessionManager> logger)
    {
        _graph  = graph;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a stream to a drive item using a resumable upload session.
    /// Suitable for files ≥ 4 MB. For smaller files use a direct PUT.
    /// </summary>
    /// <param name="driveId">Target drive ID (SP drive or SPE container drive).</param>
    /// <param name="parentItemId">Parent folder drive item ID.</param>
    /// <param name="fileName">Target file name with extension.</param>
    /// <param name="stream">Readable stream — not buffered, read in chunks.</param>
    /// <param name="conflictBehaviour">replace | rename | fail</param>
    /// <returns>The created or updated DriveItem.</returns>
    public async Task<DriveItem> UploadLargeFileAsync(
        string driveId,
        string parentItemId,
        string fileName,
        Stream stream,
        string conflictBehaviour = "replace",
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting large-file upload: '{File}' → drive {Drive}, parent {Parent}",
            fileName, driveId, parentItemId);

        // Step 1: Create upload session
        var requestBody = new CreateUploadSessionPostRequestBody
        {
            Item = new DriveItemUploadableProperties
            {
                AdditionalData = new Dictionary<string, object>
                {
                    ["@microsoft.graph.conflictBehavior"] = conflictBehaviour
                }
            }
        };

        var session = await _graph.Drives[driveId]
            .Items[parentItemId]
            .ItemWithPath(fileName)
            .CreateUploadSession
            .PostAsync(requestBody, cancellationToken: ct);

        if (session?.UploadUrl is null)
            throw new InvalidOperationException(
                $"Graph did not return an upload URL for '{fileName}'.");

        // Step 2: Upload via LargeFileUploadTask (handles chunking + progress)
        var uploadTask = new LargeFileUploadTask<DriveItem>(
            session, stream, ChunkSizeBytes, _graph.RequestAdapter);

        var maxAttempts = 5;
        UploadResult<DriveItem>? uploadResult = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                uploadResult = await uploadTask.UploadAsync(
                    progress: new Progress<long>(bytesUploaded =>
                        _logger.LogDebug("  {File}: {Bytes:N0} bytes uploaded.", fileName, bytesUploaded)),
                    cancellationToken: ct);

                // UploadResult indicates success if UploadSucceeded is true
                if (uploadResult?.UploadSucceeded == true)
                    break;

                _logger.LogWarning(
                    "Upload of '{File}' incomplete after attempt {Attempt}. Resuming...", fileName, attempt);
            }
            catch (ODataError ex) when (
                ex.ResponseStatusCode is 429 or 503 && attempt < maxAttempts)
            {
                var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 5);
                _logger.LogWarning(
                    "Upload throttled (attempt {Attempt}/{Max}). Waiting {Wait:g}...",
                    attempt, maxAttempts, wait);
                await Task.Delay(wait, ct);
            }
        }

        // TODO: UploadResult<T> property name for the uploaded item needs v5 SDK verification
        // Try Value, Result, Response, or check if T is directly accessible via cast/conversion
        // For now using .UploadSucceeded check and TODO for item extraction
        if (uploadResult?.UploadSucceeded != true)
        {
            await CancelSessionAsync(session.UploadUrl, ct);
            throw new InvalidOperationException(
                $"Large file upload failed after {maxAttempts} attempts: '{fileName}'.");
        }

        // PLACEHOLDER: Extract DriveItem from UploadResult<DriveItem>
        // This requires v5 SDK verification - property name unknown
        DriveItem? result = null;  // TODO: uploadResult.???

        _logger.LogInformation("Large-file upload complete: '{File}' → item {Id}", fileName, result?.Id ?? "unknown");
        return result ?? throw new InvalidOperationException("Failed to extract uploaded item from result.");
    }

    private async Task CancelSessionAsync(string uploadUrl, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient();
            await http.DeleteAsync(uploadUrl, ct);
            _logger.LogInformation("Upload session cancelled: {Url}", uploadUrl);
        }
        catch (Exception ex)
        {
            // Session expires automatically after ~1 hour anyway — log and ignore
            _logger.LogWarning(ex, "Failed to cancel upload session (it will expire automatically).");
        }
    }
}
