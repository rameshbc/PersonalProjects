using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Bounded in-memory queue backed by System.Threading.Channels.
/// Use this for single-replica deployments.
/// Switch to AzureServiceBusOperationQueue for multi-replica by setting
/// ServiceBus:UseServiceBus = true in configuration.
/// </summary>
public sealed class InMemoryOperationQueue : IOperationQueue
{
    private readonly Channel<ExcelOperation> _channel;
    private readonly ILogger<InMemoryOperationQueue> _logger;

    public InMemoryOperationQueue(ILogger<InMemoryOperationQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<ExcelOperation>(new BoundedChannelOptions(capacity: 1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public async ValueTask EnqueueAsync(ExcelOperation operation, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(operation, ct);
        _logger.LogDebug(
            "Enqueued {OperationType} Id={Id} WorkbookId={WorkbookId}",
            operation.GetType().Name, operation.Id, operation.WorkbookId);
    }

    public async IAsyncEnumerable<ExcelOperation> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var op in _channel.Reader.ReadAllAsync(ct))
            yield return op;
    }
}
