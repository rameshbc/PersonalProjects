using System.Collections.Concurrent;

namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// In-process result store backed by a ConcurrentDictionary of TaskCompletionSources.
/// Works for single-replica deployments. For multi-replica, replace with a Redis-backed
/// implementation — the interface contract stays identical.
/// </summary>
public sealed class InMemoryOperationResultStore : IOperationResultStore
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<object?[][]>> _pending = new();

    public void SetResult(Guid operationId, object?[][] values)
    {
        if (_pending.TryRemove(operationId, out var tcs))
            tcs.TrySetResult(values);
    }

    public void SetException(Guid operationId, Exception ex)
    {
        if (_pending.TryRemove(operationId, out var tcs))
            tcs.TrySetException(ex);
    }

    public async Task<object?[][]> WaitForResultAsync(Guid operationId, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = _pending.GetOrAdd(operationId, _ => new TaskCompletionSource<object?[][]>());

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _pending.TryRemove(operationId, out _);
            throw new TimeoutException(
                $"Extract operation {operationId} did not complete within {timeout.TotalSeconds:F0}s.");
        }
    }
}
