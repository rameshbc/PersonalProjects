using Microsoft.Extensions.Configuration;

namespace SharepointDocManager.Infrastructure.Resilience;

/// <summary>
/// Per-client SemaphoreSlim that limits the number of concurrent Graph calls
/// made on behalf of a single client (bulkhead isolation).
///
/// Why per-client bulkhead?
/// ─────────────────────────
/// Without isolation, one client uploading 500 files could consume all available
/// Graph connections, starving every other client in the system.
/// Each client gets its own concurrency slot budget — one client's load
/// never impacts another's throughput.
///
/// Default max concurrent calls per client: 4 (safe below Graph throttle cliff).
/// Configurable via "Resilience:MaxConcurrentCallsPerClient" in appsettings.
///
/// Usage:
///   var gate = _bulkheadPolicy.Get("client-001");
///   await gate.WaitAsync(ct);
///   try   { await _adapter.UploadDocumentAsync(...); }
///   finally { gate.Release(); }
/// </summary>
public sealed class BulkheadPolicy
{
    private readonly int _maxConcurrent;
    private readonly Dictionary<string, SemaphoreSlim> _gates = [];
    private readonly object _lock = new();

    public BulkheadPolicy(IConfiguration config)
    {
        _maxConcurrent = config.GetValue("Resilience:MaxConcurrentCallsPerClient", defaultValue: 4);
    }

    /// <summary>
    /// Returns (or creates) the semaphore for a given client.
    /// Thread-safe — safe to call concurrently from multiple upload tasks.
    /// </summary>
    public SemaphoreSlim Get(string clientId)
    {
        lock (_lock)
        {
            if (!_gates.TryGetValue(clientId, out var gate))
            {
                gate = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
                _gates[clientId] = gate;
            }
            return gate;
        }
    }

    /// <summary>
    /// Convenience wrapper. Acquires the semaphore, executes the work,
    /// then releases regardless of success or failure.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        string clientId,
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        var gate = Get(clientId);
        await gate.WaitAsync(ct);
        try
        {
            return await work(ct);
        }
        finally
        {
            gate.Release();
        }
    }
}
