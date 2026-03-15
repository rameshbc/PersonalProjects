using OfficeScriptWorkflow.Worker.Exceptions;
using OfficeScriptWorkflow.Worker.Services;

namespace OfficeScriptWorkflow.Worker.Workers;

/// <summary>
/// Long-running BackgroundService that consumes operations from IOperationQueue
/// and dispatches them to IExcelWorkbookService one at a time.
///
/// Processing model — one workbook at a time per replica:
/// - Operations are processed strictly sequentially: each operation runs to completion
///   before the next one is dequeued. This prevents concurrent writes to the same
///   Excel workbook, which Office Scripts do not support.
/// - In multi-replica mode (Azure Service Bus), each replica holds exactly one workbook
///   session (SessionId = WorkbookId). Different replicas process different workbooks
///   in parallel. Horizontal scaling = more replicas = more workbooks in parallel.
/// - In single-replica mode (in-memory queue), one workbook's operations are enqueued
///   and processed sequentially by the single instance.
///
/// Graceful shutdown:
/// - The stoppingToken cancels the ReadAllAsync loop.
/// - The in-flight operation completes (or is cancelled) before the host exits.
///
/// Async polling for long-running scripts:
/// - Handled transparently in the HTTP pipeline by AsyncPollingHandler.
/// - The worker simply awaits the service call — no special code needed here.
/// </summary>
public sealed class ExcelOperationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOperationQueue _queue;
    private readonly IOperationResultStore _resultStore;
    private readonly ILogger<ExcelOperationWorker> _logger;

    public ExcelOperationWorker(
        IServiceProvider services,
        IOperationQueue queue,
        IOperationResultStore resultStore,
        ILogger<ExcelOperationWorker> logger)
    {
        _services = services;
        _queue = queue;
        _resultStore = resultStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExcelOperationWorker started. Processing operations sequentially.");

        await foreach (var operation in _queue.ReadAllAsync(stoppingToken))
        {
            _logger.LogInformation(
                "Processing {OperationType} Id={Id} WorkbookId={WorkbookId}",
                operation.GetType().Name, operation.Id, operation.WorkbookId);

            await ProcessOperationAsync(operation, stoppingToken);
        }

        _logger.LogInformation("ExcelOperationWorker stopped cleanly.");
    }

    private async Task ProcessOperationAsync(ExcelOperation operation, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var workbookService = scope.ServiceProvider.GetRequiredService<IExcelWorkbookService>();

        try
        {
            await DispatchAsync(workbookService, operation, ct);

            _logger.LogInformation(
                "Completed {OperationType} Id={Id} WorkbookId={WorkbookId}",
                operation.GetType().Name, operation.Id, operation.WorkbookId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Operation {Id} cancelled during host shutdown.", operation.Id);
            _resultStore.SetException(operation.Id, new OperationCanceledException(ct));
        }
        catch (ExcelOperationException ex)
        {
            _logger.LogError(ex,
                "Script error on {OperationType} Id={Id} Sheet={Sheet} Target={Target}",
                operation.GetType().Name, operation.Id, ex.SheetName, ex.TargetName);
            _resultStore.SetException(operation.Id, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error on {OperationType} Id={Id} WorkbookId={WorkbookId}",
                operation.GetType().Name, operation.Id, operation.WorkbookId);
            _resultStore.SetException(operation.Id, ex);
        }
    }

    private async Task DispatchAsync(
        IExcelWorkbookService service,
        ExcelOperation operation,
        CancellationToken ct)
    {
        switch (operation)
        {
            case InsertRowsOperation op:
                await service.InsertTableRowsAsync(op.WorkbookId, op.SheetName, op.TableName, op.Rows, ct);
                break;

            case UpdateRangeOperation op:
                await service.UpdateRangeAsync(op.WorkbookId, op.SheetName, op.RangeAddress, op.Values, ct);
                break;

            case ExtractRangeOperation op:
                var rangeResult = await service.ExtractRangeAsync(op.WorkbookId, op.SheetName, op.RangeAddress, ct);
                _resultStore.SetResult(op.Id, rangeResult.Values);
                break;

            case ExtractDynamicArrayOperation op:
                var arrayResult = await service.ExtractDynamicArrayAsync(op.WorkbookId, op.SheetName, op.AnchorCell, ct);
                _resultStore.SetResult(op.Id, arrayResult.Values);
                break;

            default:
                throw new NotSupportedException($"Unknown operation type: {operation.GetType().Name}");
        }
    }
}
