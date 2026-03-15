using OfficeScriptWorkflow.Worker.Models.Responses;

namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Domain service for all Excel workbook operations.
/// All methods accept a workbookId that is resolved to a workbook configuration
/// (SharePoint location + flow URLs) via IWorkbookRegistry.
/// </summary>
public interface IExcelWorkbookService
{
    /// <summary>
    /// Inserts rows into an Excel table. Automatically batches large datasets
    /// to stay within the Office Script 5-minute execution limit.
    /// </summary>
    Task InsertTableRowsAsync(
        string workbookId,
        string sheetName,
        string tableName,
        object?[][] rows,
        CancellationToken ct = default);

    /// <summary>
    /// Overwrites a static range with the provided values.
    /// Values dimensions must match the range dimensions exactly.
    /// </summary>
    Task UpdateRangeAsync(
        string workbookId,
        string sheetName,
        string rangeAddress,
        object?[][] values,
        CancellationToken ct = default);

    /// <summary>Extracts data from a static A1-style range.</summary>
    Task<DynamicArrayResult> ExtractRangeAsync(
        string workbookId,
        string sheetName,
        string rangeAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts the full spill range of a dynamic array formula (FILTER, SORT, XLOOKUP, etc.).
    /// anchorCell is the cell containing the formula; the spill boundary is resolved at runtime.
    /// </summary>
    Task<DynamicArrayResult> ExtractDynamicArrayAsync(
        string workbookId,
        string sheetName,
        string anchorCell,
        CancellationToken ct = default);

    /// <summary>
    /// Executes multiple insert/update/extract operations in a single Office Script invocation.
    ///
    /// Use this instead of individual method calls when a workbook update requires many operations
    /// (e.g. 40–50 table inserts + range updates). One batch call = 3 Power Automate actions
    /// regardless of how many Excel operations it contains — reducing daily PA action consumption
    /// by up to 97% compared to one call per operation.
    ///
    /// The <paramref name="ops"/> list is processed sequentially inside the Office Script.
    /// Individual operation failures are fault-isolated — a failed op does not abort the rest.
    /// </summary>
    Task<Models.Responses.BatchScriptResult> ExecuteBatchAsync(
        string workbookId,
        IReadOnlyList<Models.Requests.BatchOp> ops,
        CancellationToken ct = default);
}
