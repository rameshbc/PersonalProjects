/**
 * ExtractRangeScript
 *
 * Extracts computed values from a static A1-style range.
 * Returns calculated values (not formulas) as a 2D array.
 *
 * For dynamic array spill extraction, use ExtractDynamicArrayScript instead.
 */
function main(
  workbook: ExcelScript.Workbook,
  sheetName: string,
  rangeAddress: string,
  includeFormulas: boolean
): {
  success: boolean;
  values: (string | number | boolean | null)[][];
  rowCount: number;
  columnCount: number;
  rangeAddress: string;
  error: string;
} {
  const empty = { success: false, values: [], rowCount: 0, columnCount: 0, rangeAddress: "", error: "" };

  try {
    const sheet = workbook.getWorksheet(sheetName);
    if (!sheet) {
      return { ...empty, error: `Sheet not found: "${sheetName}"` };
    }

    const range = sheet.getRange(rangeAddress);
    if (!range) {
      return { ...empty, error: `Range not found: "${rangeAddress}"` };
    }

    const data = includeFormulas
      ? (range.getFormulas() as (string | number | boolean | null)[][])
      : (range.getValues() as (string | number | boolean | null)[][]);

    return {
      success: true,
      values: data,
      rowCount: range.getRowCount(),
      columnCount: range.getColumnCount(),
      rangeAddress: range.getAddress(),
      error: ""
    };

  } catch (e) {
    return { ...empty, error: String(e) };
  }
}
