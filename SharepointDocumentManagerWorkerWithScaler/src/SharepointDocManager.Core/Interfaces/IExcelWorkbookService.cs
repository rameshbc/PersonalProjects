namespace SharepointDocManager.Core.Interfaces;

/// <summary>
/// Server-side Excel workbook operations using the Graph /workbook API.
/// No Excel installation required — all operations run via Graph REST.
///
/// Session pattern for multi-step edits:
///   1. OpenSessionAsync  → sessionId
///   2. ReadRangeAsync / WriteRangeAsync (pass sessionId)
///   3. CloseSessionAsync → commits all changes atomically
///
/// For read-only access, pass persistChanges = false to OpenSessionAsync.
/// No CloseSessionAsync needed for read-only sessions.
/// </summary>
public interface IExcelWorkbookService
{
    /// <summary>
    /// Creates a workbook session. Returns a session ID to pass to subsequent calls.
    /// </summary>
    /// <param name="persistChanges">
    /// true  = editable session (changes saved on CloseSessionAsync).
    /// false = read-only session (changes discarded on close).
    /// </param>
    Task<string> OpenSessionAsync(string clientId, string itemId, bool persistChanges, CancellationToken ct);

    /// <summary>Commits and closes a workbook session.</summary>
    Task CloseSessionAsync(string clientId, string itemId, string sessionId, CancellationToken ct);

    /// <summary>
    /// Reads all used cells in the specified worksheet.
    /// Returns a 2D array of cell values (rows × columns).
    /// </summary>
    Task<object[][]> ReadUsedRangeAsync(
        string clientId, string itemId, string worksheetName,
        string? sessionId, CancellationToken ct);

    /// <summary>
    /// Reads a specific named range from a worksheet.
    /// </summary>
    Task<object[][]> ReadRangeAsync(
        string clientId, string itemId, string worksheetName,
        string rangeAddress, string? sessionId, CancellationToken ct);

    /// <summary>
    /// Writes values to a range. The values array must match the range dimensions.
    /// Requires an editable session (persistChanges = true).
    /// </summary>
    Task WriteRangeAsync(
        string clientId, string itemId, string worksheetName,
        string rangeAddress, object[][] values, string sessionId, CancellationToken ct);

    /// <summary>
    /// Returns the names of all worksheets in the workbook.
    /// </summary>
    Task<IReadOnlyList<string>> ListWorksheetsAsync(
        string clientId, string itemId, string? sessionId, CancellationToken ct);
}
