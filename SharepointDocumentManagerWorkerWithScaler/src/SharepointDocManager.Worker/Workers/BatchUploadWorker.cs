using System.Threading.Channels;
using SharepointDocManager.Application.Services;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Worker.Workers;

/// <summary>
/// Background worker that drains an in-memory upload queue using a Channel.
///
/// Use case:
///   The API accepts upload requests quickly and enqueues them.
///   This worker runs concurrently, draining the queue via the bounded Channel
///   at a controlled rate — respecting Graph throttle limits.
///
/// This is the long-running worker variant. For request-scoped batch uploads
/// (where the HTTP response waits for completion) use DocumentOrchestrationService
/// directly from the controller.
///
/// Queue capacity is bounded (default 500) — producers back-pressure when full.
/// </summary>
public sealed class BatchUploadWorker : BackgroundService
{
    private readonly Channel<UploadRequest>            _queue;
    private readonly DocumentOrchestrationService      _orchestration;
    private readonly ILogger<BatchUploadWorker>        _logger;

    // Exposed for the API or other services to enqueue work
    public ChannelWriter<UploadRequest> Queue => _queue.Writer;

    public BatchUploadWorker(
        DocumentOrchestrationService orchestration,
        ILogger<BatchUploadWorker> logger)
    {
        _orchestration = orchestration;
        _logger        = logger;

        _queue = Channel.CreateBounded<UploadRequest>(new BoundedChannelOptions(500)
        {
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false   // Multiple API threads can enqueue
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[BatchUploadWorker] Started. Waiting for upload requests...");

        // Batch items into groups of up to 20 before sending to orchestration
        // to reduce Graph round-trips via $batch.
        const int batchSize = 20;
        var batch = new List<UploadRequest>(batchSize);

        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            batch.Add(request);

            // Flush when batch is full or channel is temporarily empty
            bool channelEmpty = !_queue.Reader.TryPeek(out _);
            if (batch.Count >= batchSize || channelEmpty)
            {
                await FlushBatchAsync(batch, stoppingToken);
                batch.Clear();
            }
        }

        // Drain any remaining items after channel is completed
        if (batch.Count > 0)
            await FlushBatchAsync(batch, stoppingToken);

        _logger.LogInformation("[BatchUploadWorker] Stopped.");
    }

    private async Task FlushBatchAsync(
        IReadOnlyList<UploadRequest> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        _logger.LogDebug("[BatchUploadWorker] Flushing batch of {Count} uploads.", batch.Count);
        try
        {
            var result = await _orchestration.BatchUploadAsync(batch, ct);
            if (result.HasFailures)
            {
                _logger.LogWarning(
                    "[BatchUploadWorker] Batch had {Failed}/{Total} failures.",
                    result.Failed, result.TotalRequested);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BatchUploadWorker] Batch flush error.");
        }
    }
}
