namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Stores extract operation results so callers can await them across the
/// async worker dispatch boundary.
///
/// Single-replica: in-memory ConcurrentDictionary (fast, no network hop).
/// Multi-replica: swap for Azure Cache for Redis to share results across replicas.
///   - Each result is keyed by OperationId (Guid).
///   - The worker stores the result; the originating caller polls/awaits it.
/// </summary>
public interface IOperationResultStore
{
    /// <summary>Stores a successful result for the given operation ID.</summary>
    void SetResult(Guid operationId, object?[][] values);

    /// <summary>Stores a failure for the given operation ID.</summary>
    void SetException(Guid operationId, Exception ex);

    /// <summary>
    /// Waits until a result is stored for the given operation ID, then returns it.
    /// Throws if the operation faulted or if <paramref name="timeout"/> expires.
    /// </summary>
    Task<object?[][]> WaitForResultAsync(Guid operationId, TimeSpan timeout, CancellationToken ct);
}
