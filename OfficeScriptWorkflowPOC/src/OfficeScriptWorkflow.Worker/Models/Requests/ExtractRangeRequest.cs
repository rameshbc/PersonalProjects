namespace OfficeScriptWorkflow.Worker.Models.Requests;

/// <summary>
/// Payload sent to the ExtractRange Power Automate flow.
/// Supports both static named ranges and dynamic array spill ranges.
/// </summary>
public record ExtractRangeRequest
{
    public string SheetName { get; init; } = string.Empty;

    /// <summary>
    /// For dynamic arrays: the anchor cell address (e.g. "A1") that holds the
    /// spilling formula (FILTER, SORT, XLOOKUP, etc.).
    /// Mutually exclusive with RangeAddress.
    /// </summary>
    public string? AnchorCell { get; init; }

    /// <summary>
    /// For static ranges: A1-style range address (e.g. "A1:F100").
    /// Mutually exclusive with AnchorCell.
    /// </summary>
    public string? RangeAddress { get; init; }

    /// <summary>
    /// When true, returns formula strings instead of computed values.
    /// </summary>
    public bool IncludeFormulas { get; init; } = false;
}
