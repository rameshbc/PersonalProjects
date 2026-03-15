using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OfficeScriptWorkflow.Worker.Clients;
using OfficeScriptWorkflow.Worker.Configuration;
using OfficeScriptWorkflow.Worker.Exceptions;
using OfficeScriptWorkflow.Worker.Models.Requests;
using OfficeScriptWorkflow.Worker.Models.Responses;
using OfficeScriptWorkflow.Worker.Services;

namespace OfficeScriptWorkflow.Worker.Tests;

public class ExcelWorkbookServiceTests
{
    private const string WorkbookId = "wb-01";

    private static (ExcelWorkbookService Service, Mock<IPowerAutomateClient> Client)
        Build(WorkbookInstanceConfig? config = null)
    {
        var cfg = config ?? new WorkbookInstanceConfig
        {
            Id = WorkbookId,
            DisplayName = "Test Workbook",
            SiteUrl = "https://tenant.sharepoint.com",
            WorkbookPath = "/Shared Documents/Test.xlsx",
            InsertRangeFlowUrl  = "https://flow/insert",
            UpdateRangeFlowUrl  = "https://flow/update",
            ExtractRangeFlowUrl = "https://flow/extract",
            BatchOperationFlowUrl = "https://flow/batch"
        };

        var registryOptions = Options.Create(new WorkbookRegistryOptions { Workbooks = [cfg] });
        var registry = new WorkbookRegistry(registryOptions, NullLogger<WorkbookRegistry>.Instance);

        var client = new Mock<IPowerAutomateClient>();
        var service = new ExcelWorkbookService(client.Object, registry, NullLogger<ExcelWorkbookService>.Instance);

        return (service, client);
    }

    // ── InsertTableRowsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task InsertTableRowsAsync_EmptyRows_DoesNotCallClient()
    {
        var (service, client) = Build();

        await service.InsertTableRowsAsync(WorkbookId, "Sheet1", "Table1", [], CancellationToken.None);

        client.Verify(c => c.InsertRangeAsync(It.IsAny<string>(), It.IsAny<InsertRangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InsertTableRowsAsync_SingleBatch_CallsClientOnce()
    {
        var (service, client) = Build();
        client
            .Setup(c => c.InsertRangeAsync(It.IsAny<string>(), It.IsAny<InsertRangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessInsertResponse());

        var rows = GenerateRows(3);
        await service.InsertTableRowsAsync(WorkbookId, "Sheet1", "Table1", rows);

        client.Verify(c => c.InsertRangeAsync("https://flow/insert", It.IsAny<InsertRangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InsertTableRowsAsync_RowsExceedBatchSize_CallsClientMultipleTimes()
    {
        var config = new WorkbookInstanceConfig
        {
            Id = WorkbookId, DisplayName = "Test", SiteUrl = "x", WorkbookPath = "y",
            InsertRangeFlowUrl = "https://flow/insert", BatchSize = 2
        };
        var (service, client) = Build(config);
        client
            .Setup(c => c.InsertRangeAsync(It.IsAny<string>(), It.IsAny<InsertRangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessInsertResponse());

        var rows = GenerateRows(5); // 5 rows, batch size 2 → 3 calls
        await service.InsertTableRowsAsync(WorkbookId, "Sheet1", "Table1", rows);

        client.Verify(c => c.InsertRangeAsync(It.IsAny<string>(), It.IsAny<InsertRangeRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task InsertTableRowsAsync_ScriptFails_ThrowsExcelOperationException()
    {
        var (service, client) = Build();
        client
            .Setup(c => c.InsertRangeAsync(It.IsAny<string>(), It.IsAny<InsertRangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailedInsertResponse("Column count mismatch"));

        await Assert.ThrowsAsync<ExcelOperationException>(() =>
            service.InsertTableRowsAsync(WorkbookId, "Sheet1", "Table1", GenerateRows(1)));
    }

    // ── UpdateRangeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRangeAsync_Success_CallsClientWithCorrectUrl()
    {
        var (service, client) = Build();
        client
            .Setup(c => c.UpdateRangeAsync(It.IsAny<string>(), It.IsAny<UpdateRangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessInsertResponse());

        await service.UpdateRangeAsync(WorkbookId, "Sheet1", "A1:B5", [["x", "y"]]);

        client.Verify(c => c.UpdateRangeAsync("https://flow/update", It.IsAny<UpdateRangeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRangeAsync_ScriptFails_ThrowsExcelOperationException()
    {
        var (service, client) = Build();
        client
            .Setup(c => c.UpdateRangeAsync(It.IsAny<string>(), It.IsAny<UpdateRangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailedInsertResponse("Range mismatch"));

        await Assert.ThrowsAsync<ExcelOperationException>(() =>
            service.UpdateRangeAsync(WorkbookId, "Sheet1", "A1:B5", [["x"]]));
    }

    // ── ExtractRangeAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractRangeAsync_Success_ReturnsScriptOutput()
    {
        var (service, client) = Build();
        var expected = new DynamicArrayResult { Success = true, Values = [[1, 2], [3, 4]], RowCount = 2, ColumnCount = 2, RangeAddress = "A1:B2" };
        client
            .Setup(c => c.ExtractRangeAsync(It.IsAny<string>(), It.IsAny<ExtractRangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExtractRangeResponse { Status = "success", ScriptOutput = expected });

        var result = await service.ExtractRangeAsync(WorkbookId, "Sheet1", "A1:B2");

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ExtractRangeAsync_ScriptFails_ThrowsExcelOperationException()
    {
        var (service, client) = Build();
        client
            .Setup(c => c.ExtractRangeAsync(It.IsAny<string>(), It.IsAny<ExtractRangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExtractRangeResponse { Status = "error", ScriptOutput = new DynamicArrayResult { Success = false, Error = "Spill blocked" } });

        await Assert.ThrowsAsync<ExcelOperationException>(() =>
            service.ExtractRangeAsync(WorkbookId, "Sheet1", "A1:B2"));
    }

    // ── ExecuteBatchAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteBatchAsync_NoBatchUrl_ThrowsInvalidOperation()
    {
        var config = new WorkbookInstanceConfig
        {
            Id = WorkbookId, DisplayName = "Test", SiteUrl = "x", WorkbookPath = "y",
            InsertRangeFlowUrl = "u", UpdateRangeFlowUrl = "u", ExtractRangeFlowUrl = "u",
            BatchOperationFlowUrl = "" // not configured
        };
        var (service, _) = Build(config);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteBatchAsync(WorkbookId, [], CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteBatchAsync_Success_ReturnsBatchResult()
    {
        var (service, client) = Build();
        var batchResult = new BatchScriptResult { Success = true, TotalSucceeded = 2, TotalFailed = 0 };
        client
            .Setup(c => c.ExecuteBatchAsync(It.IsAny<string>(), It.IsAny<BatchOperationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchOperationResponse { Status = "success", ScriptOutput = batchResult });

        var ops = new List<BatchOp>
        {
            new() { OperationId = "op-1", Type = "insert", SheetName = "Sheet1", TableName = "T1" },
            new() { OperationId = "op-2", Type = "update", SheetName = "Sheet2", RangeAddress = "A1:B2" }
        };

        var result = await service.ExecuteBatchAsync(WorkbookId, ops);

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalSucceeded);
        client.Verify(c => c.ExecuteBatchAsync("https://flow/batch", It.IsAny<BatchOperationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteBatchAsync_NullScriptOutput_ThrowsExcelOperationException()
    {
        var (service, client) = Build();
        client
            .Setup(c => c.ExecuteBatchAsync(It.IsAny<string>(), It.IsAny<BatchOperationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchOperationResponse { Status = "error", ScriptOutput = null });

        await Assert.ThrowsAsync<ExcelOperationException>(() =>
            service.ExecuteBatchAsync(WorkbookId, [new BatchOp { OperationId = "x", Type = "insert", SheetName = "S", TableName = "T" }]));
    }

    // ── UnknownWorkbookId ─────────────────────────────────────────────────────

    [Fact]
    public async Task InsertTableRowsAsync_UnknownWorkbookId_ThrowsKeyNotFound()
    {
        var (service, _) = Build();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.InsertTableRowsAsync("does-not-exist", "Sheet1", "Table1", GenerateRows(1)));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object?[][] GenerateRows(int count) =>
        Enumerable.Range(0, count).Select(i => new object?[] { i, $"row-{i}" }).ToArray();

    private static FlowOperationResponse SuccessInsertResponse() =>
        new() { Status = "success", ScriptOutput = new ScriptReturnValue { Success = true, RowsInserted = 1 } };

    private static FlowOperationResponse FailedInsertResponse(string error) =>
        new() { Status = "error", ScriptOutput = new ScriptReturnValue { Success = false, Error = error } };
}
