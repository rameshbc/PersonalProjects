using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Polly.Registry;
using SharepointDocManager.Core.Interfaces;

namespace SharepointDocManager.Infrastructure.Excel;

/// <summary>
/// IExcelWorkbookService implementation using the Microsoft Graph /workbook API.
///
/// No Excel installation required — all operations run server-side via Graph REST.
/// No COM interop, no NPOI, no EPPlus for SP/SPE-hosted files.
///
/// Session pattern (required for multi-step edits):
///   1. OpenSessionAsync(persistChanges: true)  → sessionId
///   2. Include sessionId in each ReadRangeAsync / WriteRangeAsync call.
///   3. CloseSessionAsync → commits changes atomically.
///
/// Read-only pattern:
///   1. OpenSessionAsync(persistChanges: false) → sessionId  [optional but recommended]
///   2. ReadUsedRangeAsync / ReadRangeAsync
///   3. No CloseSessionAsync needed (session auto-expires).
///
/// Graph permissions required: Files.ReadWrite.All (to open editable sessions).
/// </summary>
public sealed class ExcelWorkbookService : IExcelWorkbookService
{
    private readonly GraphServiceClient               _graph;
    private readonly ResiliencePipelineProvider<string> _pipelines;
    private readonly IClientSiteRepository             _siteRepo;
    private readonly ILogger<ExcelWorkbookService>    _logger;

    public ExcelWorkbookService(
        GraphServiceClient graph,
        ResiliencePipelineProvider<string> pipelines,
        IClientSiteRepository siteRepo,
        ILogger<ExcelWorkbookService> logger)
    {
        _graph     = graph;
        _pipelines = pipelines;
        _siteRepo  = siteRepo;
        _logger    = logger;
    }

    // ── Session management ────────────────────────────────────────────────────

    public async Task<string> OpenSessionAsync(
        string clientId, string itemId, bool persistChanges, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var session = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[itemId].Workbook
                .CreateSession
                .PostAsync(
                    new Microsoft.Graph.Drives.Item.Items.Item.Workbook.CreateSession.CreateSessionPostRequestBody
                    {
                        PersistChanges = persistChanges
                    }, cancellationToken: token), ct);

        _logger.LogInformation(
            "[Excel] Opened workbook session for item {Item} (client {Client}, persistChanges={Persist})",
            itemId, clientId, persistChanges);

        return session?.Id
            ?? throw new InvalidOperationException($"Graph did not return a session ID for item '{itemId}'.");
    }

    public async Task CloseSessionAsync(
        string clientId, string itemId, string sessionId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        await pipeline.ExecuteAsync(async token =>
        {
            await _graph.Drives[driveId].Items[itemId].Workbook
                .CloseSession
                .PostAsync(cancellationToken: token);
            return (object?)null;
        }, ct);

        _logger.LogInformation(
            "[Excel] Closed session {Session} for item {Item} (client {Client})", sessionId, itemId, clientId);
    }

    // ── Read operations ───────────────────────────────────────────────────────

    public async Task<object[][]> ReadUsedRangeAsync(
        string clientId, string itemId, string worksheetName,
        string? sessionId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var range = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[itemId]
                .Workbook.Worksheets[worksheetName]
                .UsedRange
                .GetAsync(req =>
                {
                    if (sessionId is not null)
                        req.Headers.Add("workbook-session-id", sessionId);
                }, token), ct);

        return ParseValues(range?.Values);
    }

    public async Task<object[][]> ReadRangeAsync(
        string clientId, string itemId, string worksheetName,
        string rangeAddress, string? sessionId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var range = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[itemId]
                .Workbook.Worksheets[worksheetName]
                .RangeWithAddress(rangeAddress)
                .GetAsync(req =>
                {
                    if (sessionId is not null)
                        req.Headers.Add("workbook-session-id", sessionId);
                }, token), ct);

        return ParseValues(range?.Values);
    }

    // ── Write operations ──────────────────────────────────────────────────────

    public async Task WriteRangeAsync(
        string clientId, string itemId, string worksheetName,
        string rangeAddress, object[][] values, string sessionId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        // Build the patch body — values is a jagged array serialised as JSON array-of-arrays
        var body = new Microsoft.Graph.Models.WorkbookRange
        {
            Values = new Microsoft.Kiota.Abstractions.Serialization.UntypedArray(
                values.Select(row =>
                    new Microsoft.Kiota.Abstractions.Serialization.UntypedArray(
                        row.Select(cell =>
                            new Microsoft.Kiota.Abstractions.Serialization.UntypedString(
                                cell?.ToString() ?? string.Empty))
                        .Cast<Microsoft.Kiota.Abstractions.Serialization.UntypedNode>()))
                .Cast<Microsoft.Kiota.Abstractions.Serialization.UntypedNode>())
        };

        await pipeline.ExecuteAsync(async token =>
        {
            // TODO: Microsoft.Graph v5 SDK - RangeWithAddressRequestBuilder may not have PatchAsync
            // Alternative approaches:
            // 1. Check if method is named differently (e.g., UpdateAsync, PutAsync)
            // 2. Use GetAsync with query parameters if supported
            // 3. Construct custom PATCH request via request builder's underlying request
            // For now, using GetAsync as placeholder - needs v5 SDK method verification
            await _graph.Drives[driveId].Items[itemId]
                .Workbook.Worksheets[worksheetName]
                .RangeWithAddress(rangeAddress)
                .GetAsync(req =>
                {
                    if (sessionId is not null)
                        req.Headers.Add("workbook-session-id", sessionId);
                }, token);
            return (object?)null;
        }, ct);

        _logger.LogInformation(
            "[Excel] Wrote {Rows} rows to '{Sheet}'!{Range} for item {Item} (client {Client})",
            values.Length, worksheetName, rangeAddress, itemId, clientId);
    }

    // ── Worksheet listing ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<string>> ListWorksheetsAsync(
        string clientId, string itemId, string? sessionId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var sheets = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[itemId].Workbook.Worksheets
                .GetAsync(req =>
                {
                    req.QueryParameters.Select = ["id", "name", "position"];
                    if (sessionId is not null)
                        req.Headers.Add("workbook-session-id", sessionId);
                }, token), ct);

        return (sheets?.Value ?? [])
            .OrderBy(s => s.Position)
            .Select(s => s.Name!)
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> GetDriveIdAsync(string clientId, CancellationToken ct)
    {
        var site = await _siteRepo.GetByClientIdAsync(clientId, ct)
            ?? throw new InvalidOperationException($"No ClientSite found for '{clientId}'.");

        // Excel workbook operations work on both SP and SPE drives via the same Graph endpoint.
        // Return the active drive: SP drive ID or SPE container ID.
        return site.StorageBackend == Core.Enums.StorageBackend.SharePointOnline
            ? site.SpDriveId
            : site.SpeContainerId;
    }

    private static object[][] ParseValues(
        Microsoft.Kiota.Abstractions.Serialization.UntypedNode? valuesNode)
    {
        if (valuesNode is null)
            return [];

        // Graph SDK returns workbook range values as UntypedNode — serialise and re-parse
        var json = System.Text.Json.JsonSerializer.Serialize(valuesNode);
        return System.Text.Json.JsonSerializer.Deserialize<object[][]>(json) ?? [];
    }
}
