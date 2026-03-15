/**
 * InsertRangeScript
 *
 * Inserts rows into an Excel table. Called by the InsertRange Power Automate flow.
 *
 * Parameters supplied by the flow's "Run script" action:
 *   sheetName  - worksheet name (exact, case-sensitive)
 *   tableName  - ListObject/Table name on that sheet
 *   rows       - 2D array of values; inner arrays must match the table's column count
 *
 * Returns a JSON-serialisable object the flow passes back to the .NET caller.
 */
function main(
  workbook: ExcelScript.Workbook,
  sheetName: string,
  tableName: string,
  rows: (string | number | boolean | null)[][]
): {
  success: boolean;
  rowsInserted: number;
  error: string;
} {
  try {
    const sheet = workbook.getWorksheet(sheetName);
    if (!sheet) {
      return { success: false, rowsInserted: 0, error: `Sheet not found: "${sheetName}"` };
    }

    const table = sheet.getTable(tableName);
    if (!table) {
      return { success: false, rowsInserted: 0, error: `Table not found: "${tableName}" on sheet "${sheetName}"` };
    }

    if (!rows || rows.length === 0) {
      return { success: true, rowsInserted: 0, error: "" };
    }

    const expectedCols = table.getColumns().length;
    for (let i = 0; i < rows.length; i++) {
      if (rows[i].length !== expectedCols) {
        return {
          success: false,
          rowsInserted: 0,
          error: `Row ${i} has ${rows[i].length} values but table has ${expectedCols} columns.`
        };
      }
    }

    // -1 appends at the end of the table body (not header, not totals).
    table.addRows(-1, rows);

    return { success: true, rowsInserted: rows.length, error: "" };

  } catch (e) {
    return { success: false, rowsInserted: 0, error: String(e) };
  }
}
