using System.Text.Json;
using OfficeScriptWorkflow.Worker.Clients;
using OfficeScriptWorkflow.Worker.Exceptions;
using OfficeScriptWorkflow.Worker.Models.Requests;
using OfficeScriptWorkflow.Worker.Models.Responses;

namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Domain service that orchestrates workbook operations.
/// Resolves per-workbook configuration (flow URLs, batch size) from IWorkbookRegistry
/// and delegates HTTP calls to IPowerAutomateClient.
/// </summary>
public sealed class ExcelWorkbookService : IExcelWorkbookService
{
    private readonly IPowerAutomateClient _client;
    private readonly IWorkbookRegistry _registry;
    private readonly ILogger<ExcelWorkbookService> _logger;

    public ExcelWorkbookService(
        IPowerAutomateClient client,
        IWorkbookRegistry registry,
        ILogger<ExcelWorkbookService> logger)
    {
        _client = client;
        _registry = registry;
        _logger = logger;
    }

    public async Task InsertTableRowsAsync(
        string workbookId,
        string sheetName,
        string tableName,
        object?[][] rows,
        CancellationToken ct = default)
    {
        if (rows.Length == 0) return;

        var cfg = _registry.Get(workbookId);

        _logger.LogInformation(
            "Inserting {TotalRows} rows → [{Workbook}].[{Sheet}].[{Table}] in batches of {BatchSize}",
            rows.Length, cfg.DisplayName, sheetName, tableName, cfg.BatchSize);

        foreach (var batch in rows.Chunk(cfg.BatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var request = new InsertRangeRequest
            {
                SheetName = sheetName,
                TableName = tableName,
                Rows = batch
            };

            var response = await _client.InsertRangeAsync(cfg.InsertRangeFlowUrl, request, ct);
            ThrowIfScriptFailed(response.ScriptOutput?.Success, response.ScriptOutput?.Error,
                workbookId, sheetName, tableName, nameof(InsertTableRowsAsync));

            _logger.LogDebug(
                "Batch of {Count} rows inserted into [{Workbook}].[{Sheet}].[{Table}].",
                batch.Length, cfg.DisplayName, sheetName, tableName);
        }
    }

    public async Task UpdateRangeAsync(
        string workbookId,
        string sheetName,
        string rangeAddress,
        object?[][] values,
        CancellationToken ct = default)
    {
        var cfg = _registry.Get(workbookId);

        _logger.LogInformation(
            "Updating [{Workbook}].[{Sheet}]!{Range} ({Rows}×{Cols})",
            cfg.DisplayName, sheetName, rangeAddress,
            values.Length, values.FirstOrDefault()?.Length ?? 0);

        var request = new UpdateRangeRequest
        {
            SheetName = sheetName,
            RangeAddress = rangeAddress,
            Values = values
        };

        var response = await _client.UpdateRangeAsync(cfg.UpdateRangeFlowUrl, request, ct);
        ThrowIfScriptFailed(response.ScriptOutput?.Success, response.ScriptOutput?.Error,
            workbookId, sheetName, rangeAddress, nameof(UpdateRangeAsync));
    }

    public async Task<DynamicArrayResult> ExtractRangeAsync(
        string workbookId,
        string sheetName,
        string rangeAddress,
        CancellationToken ct = default)
    {
        var cfg = _registry.Get(workbookId);

        _logger.LogInformation(
            "Extracting [{Workbook}].[{Sheet}]!{Range}", cfg.DisplayName, sheetName, rangeAddress);

        var request = new ExtractRangeRequest
        {
            SheetName = sheetName,
            RangeAddress = rangeAddress
        };

        var response = await _client.ExtractRangeAsync(cfg.ExtractRangeFlowUrl, request, ct);
        ThrowIfScriptFailed(response.ScriptOutput?.Success, response.ScriptOutput?.Error,
            workbookId, sheetName, rangeAddress, nameof(ExtractRangeAsync));

        return response.ScriptOutput!;
    }

    public async Task<DynamicArrayResult> ExtractDynamicArrayAsync(
        string workbookId,
        string sheetName,
        string anchorCell,
        CancellationToken ct = default)
    {
        var cfg = _registry.Get(workbookId);

        _logger.LogInformation(
            "Extracting dynamic array from [{Workbook}].[{Sheet}]!{Cell}",
            cfg.DisplayName, sheetName, anchorCell);

        var request = new ExtractRangeRequest
        {
            SheetName = sheetName,
            AnchorCell = anchorCell
        };

        var response = await _client.ExtractRangeAsync(cfg.ExtractRangeFlowUrl, request, ct);
        ThrowIfScriptFailed(response.ScriptOutput?.Success, response.ScriptOutput?.Error,
            workbookId, sheetName, anchorCell, nameof(ExtractDynamicArrayAsync));

        _logger.LogInformation(
            "Dynamic array extracted: {Rows}×{Cols} at {Address}",
            response.ScriptOutput!.RowCount, response.ScriptOutput.ColumnCount,
            response.ScriptOutput.RangeAddress);

        return response.ScriptOutput;
    }

    public async Task<BatchScriptResult> ExecuteBatchAsync(
        string workbookId,
        IReadOnlyList<BatchOp> ops,
        CancellationToken ct = default)
    {
        var cfg = _registry.Get(workbookId);

        if (string.IsNullOrEmpty(cfg.BatchOperationFlowUrl))
            throw new InvalidOperationException(
                $"WorkbookId '{workbookId}' has no BatchOperationFlowUrl configured. " +
                "Add it to WorkbookRegistry:Workbooks in appsettings.");

        _logger.LogInformation(
            "Executing batch of {Count} operation(s) on [{Workbook}]",
            ops.Count, cfg.DisplayName);

        var request = new BatchOperationRequest { Operations = [.. ops] };
        var response = await _client.ExecuteBatchAsync(cfg.BatchOperationFlowUrl, request, ct);

        var result = response.ScriptOutput
            ?? throw new ExcelOperationException(
                $"Null script output from batch flow for workbook '{workbookId}'");

        if (!result.Success)
        {
            _logger.LogWarning(
                "Batch completed with {Failed} failure(s) on [{Workbook}]. Error: {Error}",
                result.TotalFailed, cfg.DisplayName, result.Error);
        }
        else
        {
            _logger.LogInformation(
                "Batch completed. {Succeeded} succeeded on [{Workbook}].",
                result.TotalSucceeded, cfg.DisplayName);
        }

        return result;
    }

    private static void ThrowIfScriptFailed(
        bool? success, string? scriptError,
        string workbookId, string sheetName, string target, string operation)
    {
        if (success == false)
            throw new ExcelOperationException(
                $"{operation} failed on workbook '{workbookId}' [{sheetName}]/{target}: {scriptError}",
                sheetName, target);
    }
}
