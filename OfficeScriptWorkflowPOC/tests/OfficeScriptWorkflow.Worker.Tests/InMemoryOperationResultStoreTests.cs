using OfficeScriptWorkflow.Worker.Services;

namespace OfficeScriptWorkflow.Worker.Tests;

public class InMemoryOperationResultStoreTests
{
    private static InMemoryOperationResultStore CreateStore() => new();

    // ── SetResult ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetResult_AfterWaitStarts_WaitReturnsResult()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        var expected = new object?[][] { new object?[] { "A", "B" } };

        var waitTask = store.WaitForResultAsync(id, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50); // let WaitForResultAsync register the TCS
        store.SetResult(id, expected);

        var actual = await waitTask;
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task SetResult_CalledTwice_SecondCallIsNoOp()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        var first = new object?[][] { new object?[] { 1 } };
        var second = new object?[][] { new object?[] { 2 } };

        // Wait must be registered first so SetResult has a TCS to resolve.
        var waitTask = store.WaitForResultAsync(id, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50);

        store.SetResult(id, first);  // resolves the TCS; TCS is removed from dict
        store.SetResult(id, second); // key no longer in dict — silent no-op

        var result = await waitTask;
        Assert.Same(first, result);
    }

    // ── SetException ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SetException_WaitPropagatesException()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        var ex = new InvalidOperationException("script failed");

        var waitTask = store.WaitForResultAsync(id, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50);
        store.SetException(id, ex);

        await Assert.ThrowsAsync<InvalidOperationException>(() => waitTask);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task WaitForResultAsync_Timeout_ThrowsTimeoutException()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            store.WaitForResultAsync(id, TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task WaitForResultAsync_TimeoutMessage_ContainsOperationId()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            store.WaitForResultAsync(id, TimeSpan.FromMilliseconds(50), CancellationToken.None));

        Assert.Contains(id.ToString(), ex.Message);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task WaitForResultAsync_ExternalCancel_ThrowsOperationCanceled()
    {
        var store = CreateStore();
        var id = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        var waitTask = store.WaitForResultAsync(id, TimeSpan.FromSeconds(30), cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
    }

    // ── Isolation between operations ──────────────────────────────────────────

    [Fact]
    public async Task MultipleOperations_ResultsDoNotCrossContaminate()
    {
        var store = CreateStore();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var dataA = new object?[][] { new object?[] { "A" } };
        var dataB = new object?[][] { new object?[] { "B" } };

        // Register waits first so SetResult has TCS entries to resolve.
        var waitA = store.WaitForResultAsync(idA, TimeSpan.FromSeconds(5), CancellationToken.None);
        var waitB = store.WaitForResultAsync(idB, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50);

        store.SetResult(idA, dataA);
        store.SetResult(idB, dataB);

        var resultA = await waitA;
        var resultB = await waitB;

        Assert.Same(dataA, resultA);
        Assert.Same(dataB, resultB);
    }
}
