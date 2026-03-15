using System.Diagnostics;
using System.Text.Json;
using AspireContainerStarter.Contracts.Enums;
using AspireContainerStarter.Contracts.Messages;
using AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;
using AspireContainerStarter.Infrastructure.SqlServer.Data;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AspireContainerStarter.Calc2Worker.Workers;

/// <summary>
/// Processes Calc2 jobs received from the Service Bus queue.
/// Mirrors the structure of <c>Calc1JobWorker</c> for Calc2-specific logic.
/// </summary>
internal sealed class Calc2JobWorker : IMessageConsumer<Calc2JobMessage>
{
    private readonly IMessagePublisher _publisher;
    private readonly IDistributedCache _cache;
    private readonly CalculationDbContext? _db;
    private readonly ILogger<Calc2JobWorker> _logger;

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Calc2JobWorker(
        IMessagePublisher publisher,
        IDistributedCache cache,
        ILogger<Calc2JobWorker> logger,
        IServiceProvider services)
    {
        _publisher = publisher;
        _cache     = cache;
        _logger    = logger;
        _db        = services.GetService<CalculationDbContext>();
    }

    public async Task HandleAsync(
        Calc2JobMessage message,
        string messageId,
        string? correlationId,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Starting Calc2 job for Job {JobId}, StateCode {StateCode}, Entity {EntityId}, TaxYear {TaxYear}",
            message.JobId, message.StateCode, message.EntityId, message.TaxYear);

        var sw = Stopwatch.StartNew();

        await PublishProgressAsync(message.JobId, JobStatus.Running, 0, $"Starting Calc2 job ({message.StateCode})", ct);

        try
        {
            await ExecuteStageAsync(message, 1, "Collecting state tax data",      25, ct);
            await ExecuteStageAsync(message, 2, "Applying state-specific rules",   50, ct);
            await ExecuteStageAsync(message, 3, "Calculating state liability",     75, ct);
            await ExecuteStageAsync(message, 4, "Persisting state results",       100, ct);

            sw.Stop();
            await PersistResultAsync(message, JobStatus.Completed, sw.ElapsedMilliseconds, ct);

            await PublishProgressAsync(message.JobId, JobStatus.Completed, 100,
                $"Calc2 job ({message.StateCode}) complete", ct);
            _logger.LogInformation("Completed Calc2 job for Job {JobId}", message.JobId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogError(ex, "Calc2 job failed for Job {JobId}", message.JobId);
            await PersistResultAsync(message, JobStatus.Failed, sw.ElapsedMilliseconds, ct);
            await PublishProgressAsync(message.JobId, JobStatus.Failed, 0, $"Failed: {ex.Message}", ct);
            throw;
        }
        finally
        {
            await _cache.RemoveAsync(CacheKey(message.JobId), ct);
        }
    }

    private async Task ExecuteStageAsync(
        Calc2JobMessage message,
        int stage,
        string description,
        int percentComplete,
        CancellationToken ct)
    {
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

        _logger.LogDebug("Job {JobId} — Stage {Stage}: {Description}", message.JobId, stage, description);
        await Task.Delay(TimeSpan.FromSeconds(2), ct);   // TODO: replace with actual calculation

        var data = JsonSerializer.Serialize(new CheckpointData(stage, DateTimeOffset.UtcNow), _json);
        await _cache.SetStringAsync(CacheKey(message.JobId), data,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4) }, ct);

        await PublishProgressAsync(message.JobId, JobStatus.Running, percentComplete, description, ct);
    }

    private async Task PersistResultAsync(
        Calc2JobMessage message,
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
            JobType       = "Calc2",
            TaxYear       = message.TaxYear,
            EntityId      = message.EntityId,
            StateCode     = message.StateCode,
            Status        = status.ToString(),
            ResultSummary = null,
            SubmittedAt   = message.SubmittedAt,
            CompletedAt   = DateTimeOffset.UtcNow,
            DurationMs    = durationMs,
        };

        _db.CalculationResults.Add(result);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Persisted result for Job {JobId} (status={Status}, state={StateCode})",
            message.JobId, status, message.StateCode);
    }

    private async Task PublishProgressAsync(
        Guid jobId, JobStatus status, int percent, string detail, CancellationToken ct)
    {
        var progress = new JobProgressMessage(
            jobId, "Calc2", status, percent, detail, DateTimeOffset.UtcNow);
        await _publisher.PublishAsync(progress, jobId.ToString(), ct);
    }

    private static string CacheKey(Guid jobId) => $"calc2:checkpoint:{jobId}";

    private sealed record CheckpointData(int Stage, DateTimeOffset SavedAt);
}
