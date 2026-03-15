using System.Text.Json.Serialization;
using OfficeScriptWorkflow.Worker.Models.Enums;

namespace OfficeScriptWorkflow.Worker.Models.Responses;

/// <summary>
/// Base response returned by all Power Automate flows.
/// The flow's Response action serializes the Office Script return value
/// into this envelope.
/// </summary>
public record FlowOperationResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>Raw return value from the Office Script, as a JSON object.</summary>
    [JsonPropertyName("scriptOutput")]
    public ScriptReturnValue? ScriptOutput { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }

    [JsonIgnore]
    public OperationStatus OperationStatus => Status switch
    {
        "success" => OperationStatus.Success,
        "error"   => OperationStatus.FlowError,
        _         => OperationStatus.Unknown
    };
}

public record ScriptReturnValue
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("rowsInserted")]
    public int RowsInserted { get; init; }

    [JsonPropertyName("cellsUpdated")]
    public int CellsUpdated { get; init; }
}
