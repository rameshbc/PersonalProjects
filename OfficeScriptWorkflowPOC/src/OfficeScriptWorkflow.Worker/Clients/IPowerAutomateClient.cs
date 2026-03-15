using OfficeScriptWorkflow.Worker.Models.Requests;
using OfficeScriptWorkflow.Worker.Models.Responses;
// BatchOperationRequest and BatchOperationResponse are in the same namespaces above.

namespace OfficeScriptWorkflow.Worker.Clients;

/// <summary>
/// Low-level HTTP client for Power Automate flows.
/// Flow URLs are passed explicitly — this client is URL-agnostic, enabling
/// multi-workbook routing where each workbook has its own set of flow URLs.
/// </summary>
public interface IPowerAutomateClient
{
    Task<FlowOperationResponse> InsertRangeAsync(string flowUrl, InsertRangeRequest request, CancellationToken ct = default);
    Task<FlowOperationResponse> UpdateRangeAsync(string flowUrl, UpdateRangeRequest request, CancellationToken ct = default);
    Task<ExtractRangeResponse> ExtractRangeAsync(string flowUrl, ExtractRangeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Executes multiple insert/update/extract operations in a single Office Script
    /// invocation via the BatchOperations flow. Use this instead of individual calls
    /// when a workbook update requires 10+ operations — reduces Power Automate action
    /// consumption by up to 97% compared to individual calls.
    /// </summary>
    Task<BatchOperationResponse> ExecuteBatchAsync(string flowUrl, BatchOperationRequest request, CancellationToken ct = default);
}
