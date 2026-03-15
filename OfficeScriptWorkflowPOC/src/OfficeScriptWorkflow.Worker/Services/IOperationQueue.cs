namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Decouples operation producers (timers, message buses, API calls) from the worker consumer.
/// Two implementations:
///   - InMemoryOperationQueue  — single-replica, System.Threading.Channels
///   - AzureServiceBusOperationQueue — multi-replica, session-based Service Bus
/// </summary>
public interface IOperationQueue
{
    ValueTask EnqueueAsync(ExcelOperation operation, CancellationToken ct = default);
    IAsyncEnumerable<ExcelOperation> ReadAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Base class for all Excel workbook operations.
/// WorkbookId routes the operation to the correct SharePoint document.
/// In a multi-replica deployment, Service Bus sessions are keyed by WorkbookId.
/// </summary>
public abstract record ExcelOperation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Must match an Id in WorkbookRegistryOptions.Workbooks.
    /// All operations for the same workbook are processed sequentially
    /// by a single consumer to prevent Excel write collisions.
    /// </summary>
    public string WorkbookId { get; init; } = string.Empty;
}

/// <summary>
/// Extract operations carry an OperationId; the result is stored in
/// IOperationResultStore keyed by that ID, enabling the caller to await it.
/// </summary>

// Positional parameters intentionally exclude WorkbookId — it is set via the base
// class init property to avoid CS8907 shadowing warnings.

public record InsertRowsOperation(string SheetName, string TableName, object?[][] Rows)
    : ExcelOperation;

public record UpdateRangeOperation(string SheetName, string RangeAddress, object?[][] Values)
    : ExcelOperation;

public record ExtractRangeOperation(string SheetName, string RangeAddress)
    : ExcelOperation;

public record ExtractDynamicArrayOperation(string SheetName, string AnchorCell)
    : ExcelOperation;
