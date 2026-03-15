using Application.Interfaces;
using Domain.Entities;
using System.Collections.Concurrent;

namespace Infrastructure.Messaging;

/// <summary>
/// In-memory job queue for development, testing, and single-instance deployments.
/// For production multi-instance deployments, replace with Azure Service Bus or RabbitMQ.
/// Thread-safe via ConcurrentQueue.
/// </summary>
public sealed class InMemoryCalculationJobQueue : ICalculationJobQueue
{
    private readonly ConcurrentQueue<CalculationJob> _queue = new();
    private readonly ConcurrentDictionary<Guid, CalculationJob> _inFlight = new();

    public Task EnqueueAsync(CalculationJob job, CancellationToken ct = default)
    {
        _queue.Enqueue(job);
        return Task.CompletedTask;
    }

    public Task<CalculationJob?> DequeueAsync(CancellationToken ct = default)
    {
        if (_queue.TryDequeue(out var job))
        {
            _inFlight[job.Id] = job;
            return Task.FromResult<CalculationJob?>(job);
        }
        return Task.FromResult<CalculationJob?>(null);
    }

    public Task AcknowledgeAsync(Guid jobId, CancellationToken ct = default)
    {
        _inFlight.TryRemove(jobId, out _);
        return Task.CompletedTask;
    }

    public Task NackAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_inFlight.TryRemove(jobId, out var job))
            _queue.Enqueue(job);
        return Task.CompletedTask;
    }

    public int QueueDepth => _queue.Count;
    public int InFlightCount => _inFlight.Count;
}
