# High-Volume Scaling Strategy

## The Problem — The Maths First

You mentioned one workbook update requires **40–50 Power Automate calls**.
The target is **500,000 calls per day**.

Each Power Automate flow run consumes **3 Power Platform actions**:
```
  1 x HTTP trigger
  1 x Run Script (Office Script)
  1 x Response
  ─────────────────
  3 actions per call
```

| Scenario | PA calls/day | Actions/day | Per-user Premium accounts needed | Monthly licence cost |
|----------|:------------:|:-----------:|:--------------------------------:|:--------------------:|
| Current (no change) | 500,000 | 1,500,000 | **38** | ~$1,950/month |
| Batch 10 ops per call | 50,000 | 150,000 | 4 | ~$205/month |
| Batch 25 ops per call | 20,000 | 60,000 | 2 | ~$105/month |
| **Batch all 40–50 ops → 1–3 calls** | **~12,500** | **~37,500** | **1** | **~$51/month** |

**Creating 38 service accounts is not the right answer.** It is expensive, operationally
complex (38 × password rotations, 38 × connection re-authentications every 90 days,
38 × DLP policy inclusions), and it addresses the symptom instead of the root cause.

**The root cause is architectural**: one PA call per operation when a single PA call can
execute tens of operations inside one Office Script invocation.

---

## Strategy 1 — Batch Operations in the Office Script (Primary Fix)

### Why This Works

An Office Script executes inside Excel with full access to the workbook for up to 5 minutes.
There is no reason to make 40 separate HTTP calls to insert into 40 tables when one call can
carry all 40 operations and the script loops over them.

```
Current pattern (40–50 PA calls per workbook update):
  Worker → PA → Script: Insert into Sheet1/Table1      (call 1)
  Worker → PA → Script: Insert into Sheet1/Table2      (call 2)
  Worker → PA → Script: Insert into Sheet2/SalesTable  (call 3)
  ... × 47 more calls

Batch pattern (1–3 PA calls per workbook update):
  Worker → PA → Script: Insert(Sheet1/Table1, Sheet1/Table2, Sheet2/SalesTable, ... ×47)
                          + Update(Sheet3/B2:D100, Sheet4/A1:Z200)
                          + Extract(Sheet5/A1)   — all in ONE script execution
```

The Office Script processes the entire operation list in sequence within a single run.
One 202-polling HTTP call replaces 47 individual calls.

### What Changes

1. **New Office Script**: `BatchOperationScript.ts` — accepts a structured JSON payload
   containing arrays of insert, update, and extract operations
2. **New Power Automate flow**: `BatchOperations` — one flow replaces three for write-heavy scenarios
3. **New service method**: `IExcelWorkbookService.ExecuteBatchAsync()` — builds the batch payload
4. **Existing flows remain**: For single-operation calls (e.g. a single extract) the three
   existing flows are still used. Batching is additive, not a replacement.

---

## Strategy 2 — Service Account Pool (Secondary Fix)

If, after batching, daily volume still exceeds 40,000 actions for a single account
(i.e. you genuinely need more than ~13,000 batch calls per day), add a **flow account pool**.

### How It Works

Each account in the pool owns its own set of Power Automate flows — identical logic,
different SAS-signed URLs. The Worker distributes calls across accounts round-robin
or quota-aware (switches to the next account when the current one returns HTTP 429).

```
                  IFlowAccountPool
                  ┌──────────────────────────┐
                  │ Account 0:  svc-os-01     │  40,000 actions/day
                  │ Account 1:  svc-os-02     │  40,000 actions/day
                  │ Account 2:  svc-os-03     │  40,000 actions/day
                  └──────────────────────────┘
                            │
                            │ Pick next available account
                            ▼
                    PowerAutomateClient
```

The pool is quota-aware: when a 429 (Too Many Requests) response arrives with a Retry-After
header, the pool marks that account as exhausted for the remainder of the day and routes
to the next available account automatically.

### How Many Accounts After Batching?

| Post-batch calls/day | Actions/day | Accounts needed | Monthly cost |
|:--------------------:|:-----------:|:---------------:|:------------:|
| ≤13,333 | ≤40,000 | 1 | ~$51 |
| ≤26,666 | ≤80,000 | 2 | ~$102 |
| ≤40,000 | ≤120,000 | 3 | ~$153 |
| ≤66,666 | ≤200,000 | 5 | ~$255 |

With proper batching (40–50 ops → 1–3 calls), a realistic daily volume of 500,000
original operations = ~12,500 batch calls = **1 account is sufficient**.

---

## Strategy 3 — Per-Flow Plan (When to Use)

The **Power Automate per-flow plan** ($100/flow/month) is licensed per-flow, not per-user.
Each flow gets **15,000 Power Platform requests per day**.

| | Per-user Premium | Per-flow plan |
|--|:----------------:|:-------------:|
| Daily limit | 40,000 actions/user | 15,000 actions/flow |
| Good for | Multiple flows per user | High-volume single flows shared by teams |
| HTTP trigger | Yes | Yes |
| Office Scripts connector | Yes (via user connection) | Yes (service account connection) |
| Scale-out unit | Add users | Add flow instances |

**Per-flow is worse than per-user for this scenario** because its daily limit (15,000) is
lower than per-user (40,000). It is intended for flows shared across a department where
many users trigger the same flow — not for high-frequency machine-triggered automation.

**When per-flow makes sense**: you have a small number of flows (3–5) that must be shared
across many users/teams without assigning individual Premium licences.

---

## Strategy 4 — Power Automate Process Plan

The **Power Automate Process** plan ($150/bot/month) is designed for **attended and
unattended RPA** (Robotic Process Automation) — desktop flows running on a virtual machine.
It is **not relevant** for cloud flows calling Office Scripts. Do not purchase this to solve
the volume problem.

---

## Recommended Approach for 500,000 Operations/Day

```
Phase 1 — Implement BatchOperationScript (required)
  Before: 40–50 PA calls per workbook update
  After:  1–3 PA calls per workbook update
  Result: 500,000 ops → ~12,500 batch PA calls → 37,500 actions/day
  Cost:   1 × M365 E3 + Power Automate Premium = ~$51/month

Phase 2 — Add Account Pool if Phase 1 is insufficient
  If batch calls still exceed 13,333/day (unlikely after Phase 1):
  Add 1–2 additional service accounts (svc-os-02, svc-os-03)
  Cost: $51–$153/month total

Phase 3 — Monitor and right-size
  Use Power Automate admin analytics to track daily action consumption.
  Add accounts only when consistently hitting >80% of the daily quota.
```

---

## Implementation — Batch Office Script

### `office-scripts/BatchOperationScript.ts`

See the file in the `office-scripts/` directory. Key design points:

- The script accepts a single JSON payload: `{ operations: BatchOp[] }`
- Each `BatchOp` has a `type` field: `"insert"`, `"update"`, or `"extract"`
- All operations execute sequentially within the 5-minute window
- Extract results are returned in the same response alongside write acknowledgements
- Errors on individual operations are captured without stopping the rest (fault-isolated)

**Payload size limit**: Power Automate HTTP trigger body limit is 100 MB. A realistic batch of
50 operations each writing 500 rows of 10 columns (strings) ≈ 500 KB — well within limits.

**Timing guidance**: Test your specific workbook's operation times.
A rough rule: 500 row insert ≈ 5–10 seconds. 50 such operations = 250–500 seconds — close
to the 5-minute (300 second) limit. If your batches risk hitting the timeout:
- Reduce batch size to 20–25 operations per call
- Still dramatically better than 40–50 individual calls

### New Power Automate Flow: `BatchOperations`

Request schema for the new batch flow's HTTP trigger:
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
          "operationId": { "type": "string" },
          "type":        { "type": "string" },
          "sheetName":   { "type": "string" },
          "tableName":   { "type": "string" },
          "rangeAddress":{ "type": "string" },
          "anchorCell":  { "type": "string" },
          "data": {
            "type": "array",
            "items": { "type": "array", "items": {} }
          }
        }
      }
    }
  },
  "required": ["operations"]
}
```

In the flow's "Run script" action:
- Script: `BatchOperationScript`
- Parameter `operationsJson`: `@{string(triggerBody()?['operations'])}`

---

## Implementation — Service Account Pool

### Configuration (`appsettings.json`)

```json
"FlowAccountPool": {
  "Accounts": [
    {
      "AccountId": "svc-os-01",
      "InsertRangeFlowUrl":  "",
      "UpdateRangeFlowUrl":  "",
      "ExtractRangeFlowUrl": "",
      "BatchOperationFlowUrl": "",
      "DailyActionLimit": 40000
    },
    {
      "AccountId": "svc-os-02",
      "InsertRangeFlowUrl":  "",
      "UpdateRangeFlowUrl":  "",
      "ExtractRangeFlowUrl": "",
      "BatchOperationFlowUrl": "",
      "DailyActionLimit": 40000
    }
  ]
}
```

When a 429 response is received from Power Automate (quota exhausted), the pool marks
the account as `ExhaustedUntil = midnight UTC` and routes subsequent calls to the next
account. The Polly retry policy in the existing pipeline fires first — if all retries
exhaust on the same account, the pool escalates to the next account on the next call.

---

## Decision Guide

```
Q: How many raw operations per day?
│
├─ < 500,000 → Single-account with batching is sufficient
│
└─ ≥ 500,000 raw operations
       │
       Q: What is realistic batch ratio (ops per batch call)?
       │
       ├─ 20–50 ops per batch → 10,000–25,000 batch calls/day → 1 account
       │
       ├─ 10 ops per batch → 50,000 batch calls/day → 4 accounts in pool
       │
       └─ Cannot batch (all operations are independent single-row inserts)
              │
              → Account pool with quota-aware routing
              → Budget: $51/account/month
              → Each account handles 13,333 batch calls/day
              → 500,000 single calls → 38 accounts → $1,950/month
              → REVISIT whether operations can be batched at caller side
```

---

## What to Avoid

| Approach | Why to Avoid |
|----------|-------------|
| 38 service accounts, no batching | $1,950/month, 38× operational overhead, 38× connection re-auth every 90 days |
| Per-flow plan for volume | Lower limit (15K/day/flow) than per-user Premium (40K/day/user) |
| Power Automate Process plan | Designed for desktop/attended RPA, not cloud flow volume |
| Logic Apps as drop-in replacement | Logic Apps does not have the Excel Online (Business) "Run Script" connector — cannot execute Office Scripts |
| Increasing batch size beyond 5-min window | Script timeout kills the entire batch — use partial batches with error isolation |

---

## Cost Comparison Summary

| Approach | PA calls/day | Accounts | Monthly licence | Ops overhead |
|----------|:------------:|:--------:|:---------------:|:------------:|
| No change (40–50 calls each) | 500,000 | 38 | ~$1,950 | Very high |
| Batch 10 ops/call | 50,000 | 4 | ~$205 | Moderate |
| **Batch 40–50 ops/call (recommended)** | **~12,500** | **1** | **~$51** | **Minimal** |
| Pool only, no batching | 500,000 | 38 | ~$1,950 | Very high |
| Batch + small pool (safety margin) | ~12,500 | 2 | ~$102 | Minimal |
