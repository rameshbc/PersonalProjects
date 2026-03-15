using System.Text.Json.Serialization;

namespace OfficeScriptWorkflow.Worker.Models.Responses;

public record ExtractRangeResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("scriptOutput")]
    public DynamicArrayResult? ScriptOutput { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }
}

/// <summary>
/// Represents the extracted data from a range or dynamic array spill.
/// Mirrors the Office Script return type exactly.
/// </summary>
public record DynamicArrayResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// The extracted cell values as a 2D array [row][column].
    /// Types: string, double, bool, or null for empty cells.
    /// </summary>
    [JsonPropertyName("values")]
    public object?[][] Values { get; init; } = [];

    [JsonPropertyName("rowCount")]
    public int RowCount { get; init; }

    [JsonPropertyName("columnCount")]
    public int ColumnCount { get; init; }

    /// <summary>A1-style address of the actual range that was read (e.g. "Sheet1!A1:F42").</summary>
    [JsonPropertyName("rangeAddress")]
    public string RangeAddress { get; init; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}
