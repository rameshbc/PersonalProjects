/**
 * BatchOperationScript
 *
 * Executes multiple insert, update, and extract operations in a single Office Script
 * invocation, replacing what would otherwise be 40–50 individual Power Automate calls.
 *
 * WHY:
 *   Power Automate per-user Premium = 40,000 actions/day.
 *   Each PA call = 3 actions. 500,000 operations/day = 38 accounts needed.
 *   One batch call covering all 40–50 ops per workbook update = 1 account sufficient.
 *
 * INPUT (operationsJson — JSON string of BatchOperationPayload):
 *   {
 *     "operations": [
 *       { "operationId": "a1", "type": "insert", "sheetName": "Sales", "tableName": "SalesTable",
 *         "data": [["Row1Col1", 100], ["Row2Col1", 200]] },
 *       { "operationId": "b1", "type": "update", "sheetName": "Summary", "rangeAddress": "B2:D5",
 *         "data": [[1, 2, 3], [4, 5, 6], [7, 8, 9], [10, 11, 12]] },
 *       { "operationId": "c1", "type": "extract",      "sheetName": "Output", "rangeAddress": "A1:F100" },
 *       { "operationId": "d1", "type": "extractSpill", "sheetName": "Calcs",  "anchorCell": "A2" }
 *     ]
 *   }
 *
 * OUTPUT (BatchOperationResult):
 *   {
 *     "success": true,
 *     "results": [
 *       { "operationId": "a1", "success": true, "rowsAffected": 2, "error": "" },
 *       { "operationId": "b1", "success": true, "cellsAffected": 12, "error": "" },
 *       { "operationId": "c1", "success": true, "values": [[...],[...]], "rowCount": 100, "columnCount": 6, "error": "" },
 *       { "operationId": "d1", "success": true, "values": [[...]], "rowCount": 47, "columnCount": 6, "rangeAddress": "Calcs!A2:F48", "error": "" }
 *     ],
 *     "totalSucceeded": 4,
 *     "totalFailed": 0,
 *     "error": ""
 *   }
 *
 * FAULT ISOLATION:
 *   Each operation is wrapped in try/catch. A failure in operation N does NOT abort
 *   operations N+1 onward. The caller inspects each result[].success independently.
 *
 * TIMEOUT:
 *   Office Scripts time out at 5 minutes. For large batches, test your specific
 *   workbook. Rule of thumb: 500-row table insert ≈ 5–10 seconds.
 *   50 inserts × 10s = 500s — close to the limit. Reduce batch size if needed.
 */

interface BatchOp {
  operationId: string;
  type: "insert" | "update" | "extract" | "extractSpill";
  sheetName: string;
  tableName?: string;        // required for type="insert"
  rangeAddress?: string;     // required for type="update" and type="extract"
  anchorCell?: string;       // required for type="extractSpill"
  data?: (string | number | boolean | null)[][];  // required for insert/update
}

interface OpResult {
  operationId: string;
  success: boolean;
  rowsAffected?: number;
  cellsAffected?: number;
  values?: (string | number | boolean | null)[][];
  rowCount?: number;
  columnCount?: number;
  rangeAddress?: string;
  error: string;
}

interface BatchOperationResult {
  success: boolean;
  results: OpResult[];
  totalSucceeded: number;
  totalFailed: number;
  error: string;
}

function main(
  workbook: ExcelScript.Workbook,
  operationsJson: string
): BatchOperationResult {

  const failed: BatchOperationResult = {
    success: false, results: [], totalSucceeded: 0, totalFailed: 0, error: ""
  };

  // Parse the incoming JSON payload.
  let payload: { operations: BatchOp[] };
  try {
    payload = JSON.parse(operationsJson);
  } catch (e) {
    return { ...failed, error: `Failed to parse operationsJson: ${String(e)}` };
  }

  if (!payload.operations || payload.operations.length === 0) {
    return { success: true, results: [], totalSucceeded: 0, totalFailed: 0, error: "" };
  }

  const results: OpResult[] = [];

  for (const op of payload.operations) {
    const result = executeOperation(workbook, op);
    results.push(result);
  }

  const succeeded = results.filter(r => r.success).length;
  const failed_count = results.length - succeeded;

  return {
    success: failed_count === 0,
    results,
    totalSucceeded: succeeded,
    totalFailed: failed_count,
    error: failed_count > 0
      ? `${failed_count} of ${results.length} operation(s) failed. Check results[].error for details.`
      : ""
  };
}

function executeOperation(
  workbook: ExcelScript.Workbook,
  op: BatchOp
): OpResult {
  const base: OpResult = { operationId: op.operationId, success: false, error: "" };

  try {
    switch (op.type) {
      case "insert":
        return executeInsert(workbook, op, base);
      case "update":
        return executeUpdate(workbook, op, base);
      case "extract":
        return executeExtract(workbook, op, base);
      case "extractSpill":
        return executeExtractSpill(workbook, op, base);
      default:
        return { ...base, error: `Unknown operation type: "${(op as BatchOp).type}"` };
    }
  } catch (e) {
    return { ...base, error: `Unhandled exception: ${String(e)}` };
  }
}

function executeInsert(
  workbook: ExcelScript.Workbook,
  op: BatchOp,
  base: OpResult
): OpResult {
  if (!op.tableName) return { ...base, error: "tableName is required for insert operations." };
  if (!op.data || op.data.length === 0) return { ...base, success: true, rowsAffected: 0 };

  const sheet = workbook.getWorksheet(op.sheetName);
  if (!sheet) return { ...base, error: `Sheet not found: "${op.sheetName}"` };

  const table = sheet.getTable(op.tableName);
  if (!table) return { ...base, error: `Table not found: "${op.tableName}" on sheet "${op.sheetName}"` };

  const expectedCols = table.getColumns().length;
  for (let i = 0; i < op.data.length; i++) {
    if (op.data[i].length !== expectedCols) {
      return {
        ...base,
        error: `Row ${i} has ${op.data[i].length} values but table "${op.tableName}" has ${expectedCols} columns.`
      };
    }
  }

  table.addRows(-1, op.data);
  return { ...base, success: true, rowsAffected: op.data.length };
}

function executeUpdate(
  workbook: ExcelScript.Workbook,
  op: BatchOp,
  base: OpResult
): OpResult {
  if (!op.rangeAddress) return { ...base, error: "rangeAddress is required for update operations." };
  if (!op.data || op.data.length === 0) return { ...base, success: true, cellsAffected: 0 };

  const sheet = workbook.getWorksheet(op.sheetName);
  if (!sheet) return { ...base, error: `Sheet not found: "${op.sheetName}"` };

  const range = sheet.getRange(op.rangeAddress);
  const rangeRows = range.getRowCount();
  const rangeCols = range.getColumnCount();
  const dataRows = op.data.length;
  const dataCols = op.data[0]?.length ?? 0;

  if (rangeRows !== dataRows || rangeCols !== dataCols) {
    return {
      ...base,
      error: `Dimension mismatch for "${op.rangeAddress}". Range is ${rangeRows}×${rangeCols}, data is ${dataRows}×${dataCols}.`
    };
  }

  range.setValues(op.data);
  return { ...base, success: true, cellsAffected: rangeRows * rangeCols };
}

function executeExtract(
  workbook: ExcelScript.Workbook,
  op: BatchOp,
  base: OpResult
): OpResult {
  if (!op.rangeAddress) return { ...base, error: "rangeAddress is required for extract operations." };

  const sheet = workbook.getWorksheet(op.sheetName);
  if (!sheet) return { ...base, error: `Sheet not found: "${op.sheetName}"` };

  const range = sheet.getRange(op.rangeAddress);
  const values = range.getValues() as (string | number | boolean | null)[][];

  return {
    ...base,
    success: true,
    values,
    rowCount: range.getRowCount(),
    columnCount: range.getColumnCount(),
    rangeAddress: range.getAddress()
  };
}

function executeExtractSpill(
  workbook: ExcelScript.Workbook,
  op: BatchOp,
  base: OpResult
): OpResult {
  if (!op.anchorCell) return { ...base, error: "anchorCell is required for extractSpill operations." };

  const sheet = workbook.getWorksheet(op.sheetName);
  if (!sheet) return { ...base, error: `Sheet not found: "${op.sheetName}"` };

  const anchor = sheet.getRange(op.anchorCell);
  const spillRange = anchor.getSpillingToRange();

  if (!spillRange) {
    return {
      ...base,
      error: `No spill range at "${op.anchorCell}". Formula may be blocked (#SPILL!) or returned an empty result.`
    };
  }

  const values = spillRange.getValues() as (string | number | boolean | null)[][];
  return {
    ...base,
    success: true,
    values,
    rowCount: spillRange.getRowCount(),
    columnCount: spillRange.getColumnCount(),
    rangeAddress: spillRange.getAddress()
  };
}
