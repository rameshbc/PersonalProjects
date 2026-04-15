using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Interfaces;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Application.Services;

/// <summary>
/// Orchestrates parallel document uploads using a bounded Channel producer/consumer.
///
/// Why Channel vs Task.WhenAll?
/// ─────────────────────────────
/// Task.WhenAll starts ALL tasks immediately — with 500 files that's 500 concurrent
/// Graph calls, which immediately triggers throttling.
/// Channel provides backpressure: producers block when the buffer is full, ensuring
/// only MaxConsumers × batch are in-flight at any time.
///
/// Flow:
///   Producer → Channel (bounded, capacity = 2 × consumer count)
///   Consumer 1 → UploadDocumentAsync → reports progress via Action&lt;UploadProgress&gt;
///   Consumer 2 → ...
///   Consumer N → ...
///
/// Progress reporting:
///   Each completed upload calls the onProgress callback. In the API layer this
///   callback is wired to SignalR to push real-time progress to the browser.
///
/// This service implements IDocumentService — the API calls it directly.
/// </summary>
public sealed class DocumentOrchestrationService : IDocumentService
{
    private readonly StorageAdapterResolver _resolver;
    private readonly ILogger<DocumentOrchestrationService> _logger;

    // Number of concurrent upload consumers. Configurable; default 4 stays below Graph throttle cliff.
    private const int MaxConsumers = 4;

    public DocumentOrchestrationService(
        StorageAdapterResolver resolver,
        ILogger<DocumentOrchestrationService> logger)
    {
        _resolver = resolver;
        _logger   = logger;
    }

    // ── IDocumentService ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DocumentItem>> ListAsync(
        string clientId, string folderId, CancellationToken ct)
    {
        var adapter = await _resolver.ResolveAsync(clientId, ct);
        return await adapter.ListDocumentsAsync(clientId, folderId, ct);
    }

    public async Task<DocumentItem> UploadAsync(UploadRequest request, CancellationToken ct)
    {
        var adapter = await _resolver.ResolveAsync(request.ClientId, ct);
        return await adapter.UploadDocumentAsync(request, ct);
    }

    public async Task<BatchOperationResult> BatchUploadAsync(
        IEnumerable<UploadRequest> requests, CancellationToken ct)
    {
        return await BatchUploadWithProgressAsync(requests, onProgress: null, ct);
    }

    public async Task<string> GetOnlineEditUrlAsync(string clientId, string itemId, CancellationToken ct)
    {
        var adapter = await _resolver.ResolveAsync(clientId, ct);
        return await adapter.GetOnlineEditUrlAsync(clientId, itemId, ct);
    }

    // ── Channel-based parallel upload (used by API + Worker) ─────────────────

    /// <summary>
    /// Uploads requests through a bounded Channel. Calls onProgress after each item.
    /// </summary>
    public async Task<BatchOperationResult> BatchUploadWithProgressAsync(
        IEnumerable<UploadRequest> requests,
        Action<UploadProgressEvent>? onProgress,
        CancellationToken ct)
    {
        var requestList = requests.ToList();
        var results     = new List<ItemOperationResult>(requestList.Count);
        var completed   = 0;

        // Bounded channel — backpressure stops the producer when consumers are busy
        var channel = Channel.CreateBounded<UploadRequest>(new BoundedChannelOptions(MaxConsumers * 2)
        {
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });

        // Producer: write requests onto the channel
        var producer = Task.Run(async () =>
        {
            foreach (var req in requestList)
            {
                await channel.Writer.WriteAsync(req, ct);
            }
            channel.Writer.Complete();
        }, ct);

        // Consumers: N concurrent upload tasks reading from the channel
        var consumers = Enumerable.Range(0, MaxConsumers).Select(_ => Task.Run(async () =>
        {
            await foreach (var req in channel.Reader.ReadAllAsync(ct))
            {
                ItemOperationResult result;
                try
                {
                    var adapter = await _resolver.ResolveAsync(req.ClientId, ct);
                    var item    = await adapter.UploadDocumentAsync(req, ct);
                    result = new ItemOperationResult { ItemName = req.FileName, Success = true, Item = item };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Orchestration] Upload failed for '{File}'.", req.FileName);
                    result = new ItemOperationResult
                    {
                        ItemName     = req.FileName,
                        Success      = false,
                        ErrorMessage = ex.Message
                    };
                }

                lock (results) results.Add(result);

                var done = Interlocked.Increment(ref completed);
                onProgress?.Invoke(new UploadProgressEvent(req.FileName, done, requestList.Count, result.Success));
            }
        }, ct)).ToArray();

        await Task.WhenAll([producer, .. consumers]);

        return new BatchOperationResult { TotalRequested = requestList.Count, Results = results };
    }
}

/// <summary>Progress event emitted after each file completes upload.</summary>
public sealed record UploadProgressEvent(
    string FileName,
    int    Completed,
    int    Total,
    bool   Success);
