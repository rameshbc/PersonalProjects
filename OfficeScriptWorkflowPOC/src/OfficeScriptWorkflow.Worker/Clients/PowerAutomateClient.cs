using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OfficeScriptWorkflow.Worker.Configuration;
using OfficeScriptWorkflow.Worker.Models.Requests;
using OfficeScriptWorkflow.Worker.Models.Responses;
// BatchOperationRequest, BatchOperationResponse are in the same namespaces above.

namespace OfficeScriptWorkflow.Worker.Clients;

/// <summary>
/// Typed HTTP client for Power Automate HTTP-triggered flows.
///
/// URL-agnostic: the caller passes the specific flow URL, allowing multi-workbook
/// routing from a single shared HttpClient instance.
///
/// Resilience stack (applied in ServiceCollectionExtensions, outermost to innermost):
///   CircuitBreaker → Retry → AsyncPollingHandler → PowerAutomateRetryHandler → HttpClient
///
/// Long-running flows (>2 min) are handled transparently by AsyncPollingHandler
/// which follows the 202 Accepted + Location polling pattern automatically.
/// </summary>
public sealed class PowerAutomateClient : IPowerAutomateClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PowerAutomateClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public PowerAutomateClient(
        HttpClient httpClient,
        IOptions<ConcurrencyOptions> concurrencyOptions,
        ILogger<PowerAutomateClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Timeout per attempt — AsyncPollingHandler manages the total wall-clock budget.
        _httpClient.Timeout = TimeSpan.FromSeconds(concurrencyOptions.Value.HttpTimeoutSeconds);
    }

    public Task<FlowOperationResponse> InsertRangeAsync(string flowUrl, InsertRangeRequest request, CancellationToken ct = default)
        => PostAsync<InsertRangeRequest, FlowOperationResponse>(flowUrl, request, ct);

    public Task<FlowOperationResponse> UpdateRangeAsync(string flowUrl, UpdateRangeRequest request, CancellationToken ct = default)
        => PostAsync<UpdateRangeRequest, FlowOperationResponse>(flowUrl, request, ct);

    public Task<ExtractRangeResponse> ExtractRangeAsync(string flowUrl, ExtractRangeRequest request, CancellationToken ct = default)
        => PostAsync<ExtractRangeRequest, ExtractRangeResponse>(flowUrl, request, ct);

    public Task<BatchOperationResponse> ExecuteBatchAsync(string flowUrl, BatchOperationRequest request, CancellationToken ct = default)
        => PostAsync<BatchOperationRequest, BatchOperationResponse>(flowUrl, request, ct);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string flowUrl,
        TRequest payload,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug(
            "Calling Power Automate flow. Target: {FlowTarget}. Payload: {Bytes}B",
            MaskSasKey(flowUrl), Encoding.UTF8.GetByteCount(json));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(flowUrl, content, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Per-attempt timeout fired — Polly will retry.
            throw new TimeoutException(
                $"Power Automate flow did not respond within the configured HttpTimeoutSeconds.", ex);
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Flow returned non-success. Status: {Status}. Body: {Body}",
                (int)response.StatusCode, responseBody);
            response.EnsureSuccessStatusCode(); // Polly observes the HttpRequestException.
        }

        _logger.LogDebug("Flow call successful. Status: {Status}", (int)response.StatusCode);

        return JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions)
            ?? throw new InvalidOperationException(
                $"Null response from flow. Raw body: {responseBody}");
    }

    private static string MaskSasKey(string url)
    {
        var sigIndex = url.IndexOf("sig=", StringComparison.OrdinalIgnoreCase);
        if (sigIndex < 0) return url;
        var end = url.IndexOf('&', sigIndex);
        return end < 0
            ? url[..sigIndex] + "sig=***"
            : url[..sigIndex] + "sig=***" + url[end..];
    }
}
