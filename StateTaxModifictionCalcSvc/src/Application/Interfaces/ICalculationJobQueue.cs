using Domain.Entities;

namespace Application.Interfaces;

/// <summary>
/// Abstraction over the job queue — allows swapping between in-memory (dev/test),
/// Azure Service Bus, RabbitMQ, or SQL-backed queues without changing the worker.
/// </summary>
public interface ICalculationJobQueue
{
    /// <summary>Enqueue a new job for processing.</summary>
    Task EnqueueAsync(CalculationJob job, CancellationToken ct = default);

    /// <summary>
    /// Dequeue the next job.
    /// Returns null when the queue is empty.
    /// Implementations must handle visibility timeout / lock for at-least-once delivery.
    /// </summary>
    Task<CalculationJob?> DequeueAsync(CancellationToken ct = default);

    /// <summary>
    /// Acknowledge successful processing — removes the job from the queue.
    /// Called after the job is persisted as Completed.
    /// </summary>
    Task AcknowledgeAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Return a job to the queue for retry (e.g., after transient failure).
    /// </summary>
    Task NackAsync(Guid jobId, CancellationToken ct = default);
}
