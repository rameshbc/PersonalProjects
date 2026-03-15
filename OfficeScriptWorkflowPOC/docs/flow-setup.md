# Power Automate Flow Setup Guide

## Prerequisites

- Power Automate licence with Premium connectors (HTTP trigger is premium)
- OR Logic Apps if Power Automate is not available (same connector model)
- Service account with SharePoint Contribute access
- Excel workbook stored in SharePoint document library
- Office Scripts enabled in Excel for the web for the tenant

---

## Flow 1: InsertRange

### Step 1 — Create the flow
1. Power Automate → Create → Instant cloud flow → **When an HTTP request is received**
2. Paste this JSON into the "Request Body JSON Schema" field:
```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "type": "object",
  "properties": {
    "sheetName":  { "type": "string" },
    "tableName":  { "type": "string" },
    "rows": { "type": "array", "items": { "type": "array", "items": {} } }
  },
  "required": ["sheetName", "tableName", "rows"]
}
```

### Step 2 — Add "Run script" action
- Connector: **Excel Online (Business)**
- Action: **Run script**
- Location: SharePoint
- Document Library: *(your library)*
- File: *(browse to your .xlsx file)*
- Script: **InsertRangeScript** *(must be published to the workbook's script library)*
- Parameters:
  - `sheetName` → `triggerBody()?['sheetName']`
  - `tableName` → `triggerBody()?['tableName']`
  - `rows` → `triggerBody()?['rows']`

### Step 3 — Add Response actions
**Success** (Configure run after: Succeeded):
```json
{
  "status": "success",
  "scriptOutput": @{body('Run_script')?['result']?['body']?['returnValue']},
  "timestamp": "@{utcNow()}"
}
```

**Error** (Configure run after: Failed, TimedOut, Skipped):
```json
{
  "status": "error",
  "errorCode": "@{outputs('Run_script')?['statusCode']}",
  "errorMessage": "@{body('Run_script')?['error']?['message']}",
  "timestamp": "@{utcNow()}"
}
```

### Step 4 — Save and copy trigger URL
Save the flow. The trigger URL appears in the HTTP trigger card. Copy it — this is the value
for `PowerAutomate:InsertRangeFlowUrl`.

---

## Flow 2: UpdateRange

Same pattern as InsertRange. Request schema:
```json
{
  "type": "object",
  "properties": {
    "sheetName":    { "type": "string" },
    "rangeAddress": { "type": "string" },
    "values": { "type": "array", "items": { "type": "array", "items": {} } }
  },
  "required": ["sheetName", "rangeAddress", "values"]
}
```
Script: **UpdateRangeScript**

---

## Flow 3: ExtractRange

Request schema:
```json
{
  "type": "object",
  "properties": {
    "sheetName":      { "type": "string"  },
    "anchorCell":     { "type": "string"  },
    "rangeAddress":   { "type": "string"  },
    "includeFormulas":{ "type": "boolean" }
  },
  "required": ["sheetName"]
}
```

Add a **Condition** action before the "Run script":
- Condition: `empty(triggerBody()?['anchorCell'])` is **false**
- If Yes: Run **ExtractDynamicArrayScript** with `anchorCell` parameter
- If No: Run **ExtractRangeScript** with `rangeAddress` and `includeFormulas` parameters

Both branches lead to the same Response actions.

---

---

## Flow 4: BatchOperations (high-volume / recommended)

This flow replaces 40–50 individual flow calls with a single call that executes all operations
inside one Office Script run. Required when using `IExcelWorkbookService.ExecuteBatchAsync()`.

### Step 1 — Create the flow

1. Power Automate → Create → Instant cloud flow → **When an HTTP request is received**
2. Paste this JSON into the "Request Body JSON Schema" field:

```json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "type": "object",
  "properties": {
    "operations": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "operationId":  { "type": "string" },
          "type":         { "type": "string" },
          "sheetName":    { "type": "string" },
          "tableName":    { "type": "string" },
          "rangeAddress": { "type": "string" },
          "anchorCell":   { "type": "string" },
          "data": { "type": "array", "items": { "type": "array", "items": {} } }
        }
      }
    }
  },
  "required": ["operations"]
}
```

### Step 2 — Add "Run script" action

- Connector: **Excel Online (Business)**
- Action: **Run script**
- Location / Document Library / File: same as Flows 1–3 (hardcoded to the workbook)
- Script: **BatchOperationScript**
- Parameters:
  - `operationsJson` → `@{string(triggerBody()?['operations'])}`

### Step 3 — Add Response actions

**Success**:
```json
{
  "status": "success",
  "scriptOutput": @{body('Run_script')?['result']?['body']?['returnValue']},
  "timestamp": "@{utcNow()}"
}
```

**Error**:
```json
{
  "status": "error",
  "errorCode": "@{outputs('Run_script')?['statusCode']}",
  "errorMessage": "@{body('Run_script')?['error']?['message']}",
  "timestamp": "@{utcNow()}"
}
```

### Step 4 — Save and copy trigger URL

Copy the URL and set it as `WorkbookRegistry:Workbooks:N:BatchOperationFlowUrl` (via Key Vault
or `dotnet user-secrets`). If using a service account pool, each pool account needs its own
copy of this flow — repeat for every account in `FlowAccountPool:Accounts`.

> **One flow per workbook per account**: Power Automate's "Run script" action is hardcoded to
> a specific SharePoint file at design time. Each workbook requires its own set of flows.
> Each service account pool member requires its own set of flows (flows run under the owner's
> connection). For 2 workbooks × 2 pool accounts = 8 total BatchOperations flows.

---

---

## Storage Backend — Flow Configuration Differences

Flows 1–4 are created identically regardless of where the workbook is stored. The only
difference is how the **"Run script" action's file location** is specified inside Power Automate.

### SharePoint workbooks

In the "Run script" action:

| Field | Value |
|-------|-------|
| Location | SharePoint |
| Document Library | *(browse — e.g. "Shared Documents")* |
| File | *(browse to the .xlsx file)* |
| Script | *(select from the workbook's embedded scripts)* |

Each workbook can be on a **different SharePoint site**. Create a separate flow set per
workbook — the "Run script" action is hardcoded to the selected file.

### SharePoint Embedded (SPE) workbooks

SPE containers appear as SharePoint sites with a special URL. In the "Run script" action:

| Field | Value |
|-------|-------|
| Location | SharePoint |
| Document Library | *(paste the container's URL — see below)* |
| File | *(browse to the .xlsx file inside the container)* |
| Script | *(select from the workbook's embedded scripts)* |

**Container URL format**:
```
https://YOURTENANT.sharepoint.com/contentstorage/CSP_CONTAINERID
```

Retrieve the container URL from the `SiteUrl` in `appsettings.json`, or via Graph API:
```http
GET https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}
```
The response includes `webUrl` — use this as the Document Library URL in Power Automate.

**Pre-requisites for SPE flows** (these differ from SharePoint):
1. The service account (`svc-officescript`) must be added as a container member with **Writer** role — see `docs/azure-ad-setup.md`
2. The SPE container type must have the Power Automate connection authorised by a tenant admin
3. Office Scripts must be saved to the SPE-hosted workbook the same way as a SharePoint workbook (open in Excel for the Web via the container URL)

> **Tip**: To open an SPE workbook in Excel for the Web for script deployment, navigate directly to:
> `https://YOURTENANT.sharepoint.com/contentstorage/CSP_CONTAINERID/Documents/YourWorkbook.xlsx`

---

## Publishing Office Scripts to the Workbook

Office Scripts used by Power Automate must be **saved to the workbook**, not to OneDrive.

1. Open the workbook in Excel for the Web
2. Automate tab → New Script
3. Paste the script content from `office-scripts/InsertRangeScript.ts`
4. Click **Save** (saves to the workbook's embedded script library)
5. The script now appears in the "Run script" action's Script dropdown in Power Automate
6. Repeat for all four scripts

---

## Timeout Configuration

| Setting | Recommended Value | Reason |
|---------|-------------------|--------|
| Flow HTTP trigger timeout | 120s (default) | Flows wait up to 120s for sync response |
| Office Script timeout | 5 minutes (hardcoded by MS) | Cannot be changed |
| .NET HttpClient timeout | 120s | Must be ≥ flow execution time |
| Polly retry wait | Exponential 2^n + jitter | Respects Retry-After on 429 |

For very large tables (>2000 rows), increase BatchSize to reduce per-call data but keep calls under 60s.
