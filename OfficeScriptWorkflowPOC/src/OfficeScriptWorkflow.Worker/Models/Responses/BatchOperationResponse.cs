using System.Text.Json.Serialization;

namespace OfficeScriptWorkflow.Worker.Models.Responses;

/// <summary>
/// Response envelope returned by the BatchOperations Power Automate flow.
/// Maps directly to the BatchOperationResult returned by BatchOperationScript.ts.
/// </summary>
public record BatchOperationResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("scriptOutput")]
    public BatchScriptResult? ScriptOutput { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }
}

public record BatchScriptResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("results")]
    public BatchOpResult[] Results { get; init; } = [];

    [JsonPropertyName("totalSucceeded")]
    public int TotalSucceeded { get; init; }

    [JsonPropertyName("totalFailed")]
    public int TotalFailed { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}

public record BatchOpResult
{
    [JsonPropertyName("operationId")]
    public string OperationId { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("rowsAffected")]
    public int? RowsAffected { get; init; }

    [JsonPropertyName("cellsAffected")]
    public int? CellsAffected { get; init; }

    /// <summary>Populated for extract and extractSpill operations.</summary>
    [JsonPropertyName("values")]
    public object?[][]? Values { get; init; }

    [JsonPropertyName("rowCount")]
    public int? RowCount { get; init; }

    [JsonPropertyName("columnCount")]
    public int? ColumnCount { get; init; }

    [JsonPropertyName("rangeAddress")]
    public string? RangeAddress { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}
