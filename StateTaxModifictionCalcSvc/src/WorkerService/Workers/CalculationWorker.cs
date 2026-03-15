using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerService.Processors;

namespace WorkerService.Workers;

/// <summary>
/// Long-running background service that polls the job queue and dispatches
/// CalculationJobProcessor instances for each dequeued job.
///
/// Design decisions:
///   - Uses IServiceScopeFactory to create a new DI scope per job (safe for scoped dependencies)
///   - Configurable poll interval (default 5 seconds) to avoid tight loops
///   - Graceful shutdown: stops dequeuing new work on cancellation and lets the current job finish
///   - Concurrent job limit (default 4) prevents overwhelming the database
/// </summary>
public sealed class CalculationWorker : BackgroundService
{
    private readonly ICalculationJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CalculationWorker> _logger;

    private const int MaxConcurrentJobs = 4;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public CalculationWorker(
        ICalculationJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<CalculationWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CalculationWorker started.");

        using var semaphore = new SemaphoreSlim(MaxConcurrentJobs, MaxConcurrentJobs);
        var activeTasks = new List<Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _queue.DequeueAsync(stoppingToken);

                if (job is null)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                    continue;
                }

                await semaphore.WaitAsync(stoppingToken);

                var jobTask = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var processor = scope.ServiceProvider
                            .GetRequiredService<CalculationJobProcessor>();

                        await processor.ProcessAsync(job, stoppingToken);
                        await _queue.AcknowledgeAsync(job.Id, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        await _queue.NackAsync(job.Id, CancellationToken.None);
                        _logger.LogInformation("Job {JobId} returned to queue — shutdown requested.", job.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception processing job {JobId}", job.Id);
                        await _queue.NackAsync(job.Id, CancellationToken.None);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, stoppingToken);

                activeTasks.Add(jobTask);

                // Clean up completed tasks periodically
                activeTasks.RemoveAll(t => t.IsCompleted);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CalculationWorker poll loop error.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        // Wait for all in-flight jobs to finish before the process exits
        if (activeTasks.Count > 0)
        {
            _logger.LogInformation("Waiting for {Count} in-flight jobs to complete...", activeTasks.Count);
            await Task.WhenAll(activeTasks);
        }

        _logger.LogInformation("CalculationWorker stopped.");
    }
}
