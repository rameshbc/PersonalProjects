using Microsoft.Extensions.Logging.Abstractions;
using OfficeScriptWorkflow.Worker.Services;

namespace OfficeScriptWorkflow.Worker.Tests;

public class InMemoryOperationQueueTests
{
    private static InMemoryOperationQueue CreateQueue() =>
        new(NullLogger<InMemoryOperationQueue>.Instance);

    private static InsertRowsOperation MakeInsert(string workbookId = "wb-01") =>
        new("Sheet1", "Table1", [[1, 2]]) { WorkbookId = workbookId };

    // ── Enqueue and dequeue ───────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_SingleOp_DequeueReturnsIt()
    {
        var queue = CreateQueue();
        var op = MakeInsert();

        await queue.EnqueueAsync(op);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var dequeued = await queue.ReadAllAsync(cts.Token).FirstAsync(cts.Token);

        Assert.Equal(op.Id, dequeued.Id);
    }

    [Fact]
    public async Task EnqueueAsync_MultipleOps_AllDequeued()
    {
        var queue = CreateQueue();
        var ops = Enumerable.Range(0, 5)
            .Select(_ => MakeInsert())
            .ToList();

        foreach (var op in ops)
            await queue.EnqueueAsync(op);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var results = new List<ExcelOperation>();

        try
        {
            await foreach (var op in queue.ReadAllAsync(cts.Token))
            {
                results.Add(op);
                if (results.Count == 5) break;
            }
        }
        catch (OperationCanceledException) { }

        Assert.Equal(5, results.Count);
        Assert.All(ops, original => Assert.Contains(results, r => r.Id == original.Id));
    }

    [Fact]
    public async Task EnqueueAsync_DifferentOperationTypes_PreservesType()
    {
        var queue = CreateQueue();
        var insert = new InsertRowsOperation("Sheet1", "Table1", [[1]]) { WorkbookId = "wb-01" };
        var update = new UpdateRangeOperation("Sheet1", "A1:B2", [["x"]]) { WorkbookId = "wb-01" };
        var extract = new ExtractRangeOperation("Sheet1", "A1:C10") { WorkbookId = "wb-01" };

        await queue.EnqueueAsync(insert);
        await queue.EnqueueAsync(update);
        await queue.EnqueueAsync(extract);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var results = new List<ExcelOperation>();

        try
        {
            await foreach (var op in queue.ReadAllAsync(cts.Token))
            {
                results.Add(op);
                if (results.Count == 3) break;
            }
        }
        catch (OperationCanceledException) { }

        Assert.Single(results.OfType<InsertRowsOperation>());
        Assert.Single(results.OfType<UpdateRangeOperation>());
        Assert.Single(results.OfType<ExtractRangeOperation>());
    }

    // ── WorkbookId is preserved ───────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_WorkbookIdPreservedThroughQueue()
    {
        var queue = CreateQueue();
        var op = MakeInsert("my-special-workbook");

        await queue.EnqueueAsync(op);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var dequeued = await queue.ReadAllAsync(cts.Token).FirstAsync(cts.Token);

        Assert.Equal("my-special-workbook", dequeued.WorkbookId);
    }

    // ── Cancellation of reader ────────────────────────────────────────────────

    [Fact]
    public async Task ReadAllAsync_CancelledBeforeItem_ThrowsOrCompletes()
    {
        var queue = CreateQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in queue.ReadAllAsync(cts.Token)) { }
        });
    }
}
