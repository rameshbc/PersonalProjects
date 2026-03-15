namespace OfficeScriptWorkflow.Worker.Models.Requests;

/// <summary>
/// Payload sent to the UpdateRange Power Automate flow.
/// rangeAddress uses A1-style notation (e.g. "B2:D10").
/// Values dimensions must exactly match the range dimensions.
/// </summary>
public record UpdateRangeRequest
{
    public string SheetName { get; init; } = string.Empty;
    public string RangeAddress { get; init; } = string.Empty;

    /// <summary>
    /// Values to write. Rows × Columns must match RangeAddress exactly.
    /// </summary>
    public object?[][] Values { get; init; } = [];
}
