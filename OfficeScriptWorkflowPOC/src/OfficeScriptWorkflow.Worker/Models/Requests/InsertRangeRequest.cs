namespace OfficeScriptWorkflow.Worker.Models.Requests;

/// <summary>
/// Payload sent to the InsertRange Power Automate flow.
/// The flow passes sheetName, tableName, and rows to the Office Script.
/// </summary>
public record InsertRangeRequest
{
    public string SheetName { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;

    /// <summary>
    /// Row data as a jagged array. Each inner array is one row;
    /// values must match the column order of the target table.
    /// Supported types: string, double, bool, null.
    /// </summary>
    public object?[][] Rows { get; init; } = [];
}
