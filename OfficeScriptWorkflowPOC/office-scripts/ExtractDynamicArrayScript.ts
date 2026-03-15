/**
 * ExtractDynamicArrayScript
 *
 * Extracts the full spill range of a dynamic array formula.
 *
 * Dynamic array functions (FILTER, SORT, SORTBY, UNIQUE, SEQUENCE, XLOOKUP, etc.)
 * write their results into adjacent cells — the "spill range". The spill boundary
 * changes at runtime based on the formula result.
 *
 * anchorCell: the single cell that contains the array formula (e.g. "A2").
 *             getSpillingToRange() follows the spill boundary automatically.
 *
 * Important: if another value blocks the spill, Excel shows a #SPILL! error
 * and getSpillingToRange() returns null. This script surfaces that as an error
 * string so the .NET caller can handle it explicitly.
 */
function main(
  workbook: ExcelScript.Workbook,
  sheetName: string,
  anchorCell: string
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

    const anchor = sheet.getRange(anchorCell);
    if (!anchor) {
      return { ...empty, error: `Anchor cell not found: "${anchorCell}"` };
    }

    // getSpillingToRange() returns the full spill range including the anchor cell.
    // Returns null if the formula has not spilled (e.g. #SPILL! error or empty result).
    const spillRange = anchor.getSpillingToRange();

    if (!spillRange) {
      // Check if there is actually a spill error in the anchor cell.
      const formulaLocal = anchor.getFormulaLocal();
      return {
        ...empty,
        error: formulaLocal
          ? `No spill range at "${anchorCell}". Formula may be blocked (#SPILL!) or returned empty.`
          : `Cell "${anchorCell}" does not contain a dynamic array formula.`
      };
    }

    const values = spillRange.getValues() as (string | number | boolean | null)[][];

    return {
      success: true,
      values: values,
      rowCount: spillRange.getRowCount(),
      columnCount: spillRange.getColumnCount(),
      rangeAddress: spillRange.getAddress(),  // e.g. "Sheet1!A2:F47"
      error: ""
    };

  } catch (e) {
    return { ...empty, error: String(e) };
  }
}
