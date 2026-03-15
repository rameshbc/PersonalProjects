using System.Diagnostics;
using System.Text.Json;
using AspireContainerStarter.Contracts.Enums;
using AspireContainerStarter.Contracts.Messages;
using AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;
using AspireContainerStarter.Infrastructure.SqlServer.Data;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AspireContainerStarter.Calc1Worker.Workers;

/// <summary>
/// Processes Calc1 jobs received from the Service Bus queue.
///
/// Per-message lifecycle:
///   1. Check Redis for a cached partial result (supports resume on restart).
///   2. Execute the calculation in stages, reporting progress via
///      <see cref="IMessagePublisher"/> after each stage.
///   3. Persist the final result to Azure SQL via <see cref="CalculationDbContext"/>.
///   4. Publish a final Completed/Failed progress event.
/// </summary>
internal sealed class Calc1JobWorker : IMessageConsumer<Calc1JobMessage>
{
    private readonly IMessagePublisher _publisher;
    private readonly IDistributedCache _cache;
    private readonly CalculationDbContext? _db;
    private readonly ILogger<Calc1JobWorker> _logger;

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Calc1JobWorker(
        IMessagePublisher publisher,
        IDistributedCache cache,
        ILogger<Calc1JobWorker> logger,
        IServiceProvider services)
    {
        _publisher = publisher;
        _cache     = cache;
        _logger    = logger;
        _db        = services.GetService<CalculationDbContext>();
    }

    public async Task HandleAsync(
        Calc1JobMessage message,
        string messageId,
        string? correlationId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Starting Calc1 job for Job {JobId}, Entity {EntityId}, TaxYear {TaxYear}",
            message.JobId, message.EntityId, message.TaxYear);

        var sw = Stopwatch.StartNew();

        await PublishProgressAsync(message.JobId, JobStatus.Running, 0, "Starting Calc1 job", ct);

        try
        {
            // Stage 1 — data collection (25 %)
            await ExecuteStageAsync(message, 1, "Collecting tax data",           25, ct);
            // Stage 2 — deduction calculation (50 %)
            await ExecuteStageAsync(message, 2, "Calculating deductions",        50, ct);
            // Stage 3 — liability calculation (75 %)
            await ExecuteStageAsync(message, 3, "Calculating liability",         75, ct);
            // Stage 4 — final validation and persistence (100 %)
            await ExecuteStageAsync(message, 4, "Persisting results",           100, ct);

            sw.Stop();
            await PersistResultAsync(message, JobStatus.Completed, sw.ElapsedMilliseconds, ct);

            await PublishProgressAsync(message.JobId, JobStatus.Completed, 100, "Calc1 job complete", ct);
            _logger.LogInformation("Completed Calc1 job for Job {JobId}", message.JobId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "Calc1 job failed for Job {JobId}", message.JobId);
            await PersistResultAsync(message, JobStatus.Failed, sw.ElapsedMilliseconds, ct);
            await PublishProgressAsync(message.JobId, JobStatus.Failed, 0, $"Failed: {ex.Message}", ct);
            throw;   // Re-throw so the processor abandons and retries.
        }
        finally
        {
            await _cache.RemoveAsync(CacheKey(message.JobId), ct);
        }
    }

    private async Task ExecuteStageAsync(
        Calc1JobMessage message,
        int stage,
        string description,
        int percentComplete,
        CancellationToken ct)
    {
        // Check cache for a checkpoint (resume support after crash).
        var checkpoint = await _cache.GetStringAsync(CacheKey(message.JobId), ct);
        if (checkpoint is not null)
        {
            var saved = JsonSerializer.Deserialize<CheckpointData>(checkpoint, _json);
            if (saved?.Stage >= stage)
            {
                _logger.LogDebug("Skipping stage {Stage} for Job {JobId} — already completed", stage, message.JobId);
                return;
            }
        }

        // Simulate long-running work — replace with real calculation logic.
        _logger.LogDebug("Job {JobId} — Stage {Stage}: {Description}", message.JobId, stage, description);
        await Task.Delay(TimeSpan.FromSeconds(2), ct);   // TODO: replace with actual calculation

        // Persist checkpoint to Redis.
        var data = JsonSerializer.Serialize(new CheckpointData(stage, DateTimeOffset.UtcNow), _json);
        await _cache.SetStringAsync(CacheKey(message.JobId), data,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4) }, ct);

        await PublishProgressAsync(message.JobId, JobStatus.Running, percentComplete, description, ct);
    }

    private async Task PersistResultAsync(
        Calc1JobMessage message,
        JobStatus status,
        long durationMs,
        CancellationToken ct)
    {
        if (_db is null)
        {
            _logger.LogDebug("No database configured — skipping result persistence for Job {JobId}", message.JobId);
            return;
        }

        var result = new CalculationResult
        {
            JobId         = message.JobId,
            JobType       = "Calc1",
            TaxYear       = message.TaxYear,
            EntityId      = message.EntityId,
            StateCode     = null,
            Status        = status.ToString(),
            ResultSummary = null,
            SubmittedAt   = message.SubmittedAt,
            CompletedAt   = DateTimeOffset.UtcNow,
            DurationMs    = durationMs,
        };

        _db.CalculationResults.Add(result);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Persisted result for Job {JobId} (status={Status})", message.JobId, status);
    }

    private async Task PublishProgressAsync(
        Guid jobId, JobStatus status, int percent, string detail, CancellationToken ct)
    {
        var progress = new JobProgressMessage(
            jobId, "Calc1", status, percent, detail, DateTimeOffset.UtcNow);
        await _publisher.PublishAsync(progress, jobId.ToString(), ct);
    }

    private static string CacheKey(Guid jobId) => $"calc1:checkpoint:{jobId}";

    private sealed record CheckpointData(int Stage, DateTimeOffset SavedAt);
}
