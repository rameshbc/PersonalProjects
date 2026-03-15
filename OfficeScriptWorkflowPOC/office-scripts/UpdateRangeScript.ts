/**
 * UpdateRangeScript
 *
 * Overwrites a static range with new values.
 * The values 2D array must exactly match the range's row × column dimensions.
 * Formulas are not written — use string values prefixed with "=" only if intentional.
 */
function main(
  workbook: ExcelScript.Workbook,
  sheetName: string,
  rangeAddress: string,
  values: (string | number | boolean | null)[][]
): {
  success: boolean;
  cellsUpdated: number;
  error: string;
} {
  try {
    const sheet = workbook.getWorksheet(sheetName);
    if (!sheet) {
      return { success: false, cellsUpdated: 0, error: `Sheet not found: "${sheetName}"` };
    }

    const range = sheet.getRange(rangeAddress);
    if (!range) {
      return { success: false, cellsUpdated: 0, error: `Range not found: "${rangeAddress}"` };
    }

    const rangeRows = range.getRowCount();
    const rangeCols = range.getColumnCount();
    const dataRows = values.length;
    const dataCols = values.length > 0 ? values[0].length : 0;

    if (rangeRows !== dataRows || rangeCols !== dataCols) {
      return {
        success: false,
        cellsUpdated: 0,
        error: `Dimension mismatch. Range is ${rangeRows}×${rangeCols} but data is ${dataRows}×${dataCols}.`
      };
    }

    range.setValues(values);

    return { success: true, cellsUpdated: rangeRows * rangeCols, error: "" };

  } catch (e) {
    return { success: false, cellsUpdated: 0, error: String(e) };
  }
}
