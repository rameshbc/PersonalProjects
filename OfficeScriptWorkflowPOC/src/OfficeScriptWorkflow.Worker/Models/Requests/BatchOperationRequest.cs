using System.Text.Json.Serialization;

namespace OfficeScriptWorkflow.Worker.Models.Requests;

/// <summary>
/// Payload sent to the BatchOperations Power Automate flow.
/// One HTTP call replaces what would otherwise be 40–50 individual flow calls.
///
/// The flow passes this as operationsJson (JSON string) to BatchOperationScript.ts
/// because the "Run script" action does not support dynamic object arrays natively —
/// serialising to a JSON string and deserialising in the script is the reliable pattern.
/// </summary>
public record BatchOperationRequest
{
    [JsonPropertyName("operations")]
    public BatchOp[] Operations { get; init; } = [];
}

public record BatchOp
{
    /// <summary>Caller-assigned ID echoed back in the result for correlation.</summary>
    [JsonPropertyName("operationId")]
    public string OperationId { get; init; } = string.Empty;

    /// <summary>"insert" | "update" | "extract" | "extractSpill"</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("sheetName")]
    public string SheetName { get; init; } = string.Empty;

    /// <summary>Required for insert. Must match the Excel Table name exactly.</summary>
    [JsonPropertyName("tableName")]
    public string? TableName { get; init; }

    /// <summary>Required for update and extract. A1-style notation.</summary>
    [JsonPropertyName("rangeAddress")]
    public string? RangeAddress { get; init; }

    /// <summary>Required for extractSpill. Cell containing the dynamic array formula.</summary>
    [JsonPropertyName("anchorCell")]
    public string? AnchorCell { get; init; }

    /// <summary>Row data for insert/update. Null for extract operations.</summary>
    [JsonPropertyName("data")]
    public object?[][]? Data { get; init; }
}
