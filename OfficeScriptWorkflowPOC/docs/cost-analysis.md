# Cost Analysis

> **Pricing notice**: All figures use Microsoft/Azure list prices as of early 2026 (USD).
> Enterprise Agreement, CSP, or existing licence inclusions will reduce these numbers.
> Verify current pricing at [azure.microsoft.com/pricing](https://azure.microsoft.com/pricing)
> and [powerplatform.microsoft.com/pricing](https://powerplatform.microsoft.com/pricing)
> before committing to a budget. Annual commitments typically save 15–20% vs monthly.

---

## 1. Licence Unit Costs

### Microsoft 365 — Service Account

| SKU | Monthly (per user) | Annual (per user) | Includes Office Scripts | Includes PA Premium |
|-----|:-----------------:|:-----------------:|:-----------------------:|:-------------------:|
| M365 Business Basic | $6 | $72 | No | No |
| M365 Business Standard | $12.50 | $150 | No | No |
| M365 Business Premium | $22 | $264 | No | No |
| **M365 E3** | **$36** | **$432** | **Yes** | No (standard only) |
| M365 E5 | $57 | $684 | Yes | No (standard only) |
| M365 F3 (Firstline) | $10 | $120 | No | No |

> Office Scripts is **not available** in any Business-tier plan. E3 is the minimum.

### Power Automate Add-ons

| Plan | Monthly (per user/flow) | Annual | HTTP Trigger | Daily Action Limit | Best For |
|------|:-----------------------:|:------:|:------------:|:------------------:|---------|
| Seeded (included in M365) | $0 | $0 | No | 6,000 | Simple approvals |
| **Per-user Premium** | **$15** | **$180** | **Yes** | **40,000** | **Service accounts, automation** |
| Per-flow plan | $100/flow | $1,200/flow | Yes | 15,000/flow | Shared departmental flows |
| Power Automate Process | $150/bot | $1,800/bot | Yes | Capacity-based | Desktop/attended RPA only |

> **Key insight**: Per-flow (15,000/day) has a lower daily limit than per-user Premium
> (40,000/day). For machine-triggered automation, per-user Premium is always the better value.

### Combined Service Account Cost

| Configuration | Monthly | Annual |
|--------------|:-------:|:------:|
| M365 E3 only (no PA Premium) | $36 | $432 |
| **M365 E3 + PA per-user Premium** | **$51** | **$612** |
| M365 E5 + PA per-user Premium | $72 | $864 |

---

## 2. Azure Infrastructure Costs

These costs apply regardless of volume tier and are largely fixed or near-fixed.

### Azure Key Vault (Standard tier)

| Usage | Unit Cost | Estimated Monthly |
|-------|-----------|:-----------------:|
| Secret operations (get/list) | $0.03 per 10,000 | ~$0.10 |
| Certificate operations | $3.00 per 10,000 | N/A (not used) |
| **Estimated monthly** | | **< $1** |

Key Vault cost is negligible. Use Standard tier — Premium tier (HSM-backed) is not required
unless your security policy mandates it (adds ~$5/key/month).

### Azure Container Registry

| Tier | Monthly | Storage included | Notes |
|------|:-------:|:----------------:|-------|
| Basic | $5 | 10 GB | Sufficient for single-region |
| Standard | $20 | 100 GB | Use if geo-replication needed |
| Premium | $50 | 500 GB | Use if private link required |

**Recommended**: Basic — $5/month.

### Azure Container Apps (Worker Service Hosting)

Container Apps uses a consumption model: you pay for vCPU-seconds and GiB-seconds while
containers are active. Workers run continuously (long-running BackgroundService).

| Resource | Unit Price | Estimated usage | Monthly |
|----------|-----------|:---------------:|:-------:|
| vCPU (active) | $0.000024/vCPU-second | 0.25 vCPU × 2,592,000s | ~$15.55 |
| Memory (active) | $0.000003/GiB-second | 0.5 GiB × 2,592,000s | ~$3.89 |
| Free grant | — | 180,000 vCPU-s, 360,000 GiB-s/month | -$4.32 + -$1.08 |
| **Estimated per replica/month** | | | **~$14** |

| Replicas | Monthly hosting cost |
|:--------:|:--------------------:|
| 1 | ~$14 |
| 2 | ~$28 |
| 3 | ~$42 |
| 5 | ~$70 |

> **AKS alternative**: For existing AKS clusters, the marginal cost per worker pod is
> ~$8–12/month (assuming shared node pool). No additional licensing needed.

> **Windows Service (on-premises)**: Hosting cost = 0 (uses existing VM), but you still pay
> for the VM and must manage OS patching. Factor in ~2 hours/month of admin time.

### Azure Service Bus (Standard tier — multi-replica only)

| Resource | Unit Cost | Estimated monthly usage | Monthly |
|----------|-----------|:-----------------------:|:-------:|
| Namespace | $10/month flat | — | $10 |
| Operations | $0.10 per 1 million | 12,500 batch calls × 2 (send+receive) = 25,000 ops | < $0.01 |
| **Total** | | | **~$10** |

> Standard tier supports sessions (required for workbook-based ordering). Premium tier
> ($670+/month) is only needed if you require private endpoints or geo-disaster recovery.

### Azure Monitor / Application Insights

| Resource | Unit Cost | Estimated monthly ingestion | Monthly |
|----------|-----------|:---------------------------:|:-------:|
| Log ingestion | $2.30/GB (pay-as-you-go) | ~2 GB (Info-level, 12,500 batch calls/day) | ~$4.60 |
| Log retention (90 days) | Free | — | $0 |
| Alerts (first 1,000/month) | Free | — | $0 |
| **Total** | | | **~$5** |

Reduce ingestion cost by setting `Serilog:MinimumLevel:Default = Warning` in production.
At Warning level: ~0.3 GB/month → < $1.

---

## 3. Volume Scenarios — Full Cost Breakdown

The following scenarios assume:
- **500,000 raw Excel operations per day** (your target)
- **40–50 operations per workbook update** (your stated figure)
- **3 Power Platform actions per PA flow run** (trigger + run script + response)
- Service account cost: M365 E3 ($36) + PA per-user Premium ($15) = **$51/user/month**
- Single region, no geo-redundancy

---

### Scenario A — No Change (Individual Calls, No Batching)

Each of the 500,000 operations is a separate Power Automate call.

| Item | Calculation | Monthly Cost |
|------|-------------|:------------:|
| PA actions/day | 500,000 × 3 = 1,500,000 | — |
| Accounts needed | 1,500,000 ÷ 40,000 = **37.5 → 38 accounts** | — |
| Service account licences | 38 × $51 | **$1,938** |
| Azure Container Apps (3 replicas) | 3 × $14 | $42 |
| Azure Service Bus | $10 | $10 |
| Azure Key Vault | $1 | $1 |
| Azure Container Registry | $5 | $5 |
| Azure Monitor | $5 | $5 |
| **Total monthly** | | **$2,006** |
| **Total annual** | | **$24,072** |

**Operational overhead** (not captured in licence cost):
- 38 × password rotation every 90 days = 152 rotation events/year
- 38 × connection re-authentication events = 38 sessions in Power Automate portal each cycle
- 38 × DLP policy inclusion reviews
- Estimated admin time: ~4 hours/rotation cycle × 4 cycles/year × $100/hr = **$1,600/year**

**True annual cost: ~$25,672**

---

### Scenario B — Batch 10 Operations Per Call

Group every 10 operations into one batch call. Reduces calls from 500,000 to 50,000.

| Item | Calculation | Monthly Cost |
|------|-------------|:------------:|
| PA actions/day | 50,000 × 3 = 150,000 | — |
| Accounts needed | 150,000 ÷ 40,000 = **3.75 → 4 accounts** | — |
| Service account licences | 4 × $51 | **$204** |
| Azure Container Apps (2 replicas) | 2 × $14 | $28 |
| Azure Service Bus | $10 | $10 |
| Azure Key Vault | $1 | $1 |
| Azure Container Registry | $5 | $5 |
| Azure Monitor | $5 | $5 |
| **Total monthly** | | **$253** |
| **Total annual** | | **$3,036** |

Operational overhead: 4 accounts → manageable. Admin time: ~$160/year.

**True annual cost: ~$3,196**

---

### Scenario C — Batch All 40–50 Operations Per Call ✅ Recommended

All 40–50 operations for a single workbook update are sent in one batch call.
500,000 raw operations → ~12,500 batch calls per day.

| Item | Calculation | Monthly Cost |
|------|-------------|:------------:|
| PA actions/day | 12,500 × 3 = 37,500 | — |
| Accounts needed | 37,500 ÷ 40,000 = **0.94 → 1 account** | — |
| Service account licence | 1 × $51 | **$51** |
| Azure Container Apps (1–2 replicas) | 1–2 × $14 | $14–$28 |
| Azure Service Bus | $10 (optional if single replica) | $0–$10 |
| Azure Key Vault | $1 | $1 |
| Azure Container Registry | $5 | $5 |
| Azure Monitor | $5 | $5 |
| **Total monthly (single replica, no SB)** | | **$76** |
| **Total monthly (2 replicas + SB)** | | **$100** |
| **Total annual (single replica)** | | **$912** |
| **Total annual (2 replicas + SB)** | | **$1,200** |

Operational overhead: 1 account → minimal. Admin time: ~$40/year.

**True annual cost: ~$952 – $1,240**

---

### Scenario D — Batch + Small Safety Pool (2 Accounts)

Batch all operations + 2 accounts in pool for burst capacity or failover.
Acts as a safety net if one account hits an unexpected quota spike.

| Item | Calculation | Monthly Cost |
|------|-------------|:------------:|
| Service account licences | 2 × $51 | **$102** |
| Azure Container Apps (2 replicas) | 2 × $14 | $28 |
| Azure Service Bus | $10 | $10 |
| Azure Key Vault | $1 | $1 |
| Azure Container Registry | $5 | $5 |
| Azure Monitor | $5 | $5 |
| **Total monthly** | | **$151** |
| **Total annual** | | **$1,812** |

**True annual cost: ~$1,852**

---

## 4. Scenario Comparison — Side by Side

| | Scenario A | Scenario B | Scenario C | Scenario D |
|--|:----------:|:----------:|:----------:|:----------:|
| **Strategy** | No change | Batch 10 ops | **Batch all 40–50** | Batch all + pool |
| PA calls/day | 500,000 | 50,000 | ~12,500 | ~12,500 |
| Actions/day | 1,500,000 | 150,000 | 37,500 | 37,500 |
| Service accounts | 38 | 4 | **1** | 2 |
| Monthly licence | $1,938 | $204 | **$51** | $102 |
| Monthly Azure | $63 | $49 | $25–$49 | $49 |
| **Monthly total** | **$2,001** | **$253** | **$76–$100** | **$151** |
| **Annual total** | **$24,012** | **$3,036** | **$912–$1,200** | **$1,812** |
| Admin overhead/yr | ~$1,600 | ~$160 | **~$40** | ~$80 |
| **True annual TCO** | **~$25,612** | **~$3,196** | **~$952–$1,240** | **~$1,892** |
| Complexity | Very high | Moderate | **Low** | Low |
| Connection re-auths/yr | 152 events | 16 events | **4 events** | 8 events |

---

## 5. Savings Analysis — Batching vs No Change

| Metric | Scenario A (baseline) | Scenario C (recommended) | Annual saving |
|--------|:---------------------:|:------------------------:|:-------------:|
| Monthly licence cost | $1,938 | $51 | **$22,644** |
| Monthly Azure cost | $63 | ~$37 | **$312** |
| Admin overhead/year | $1,600 | $40 | **$1,560** |
| **Total annual saving** | | | **$24,516** |
| **Saving vs baseline** | | | **96%** |

The cost of implementing batching (developer time, testing, Office Script update, new PA flow)
is recovered within the **first month** at Scenario A pricing.

---

## 6. Licensing Cost by Scale

Not all organisations start at 500,000 operations/day. The table below shows licence cost
(Scenario C — fully batched) across different raw operation volumes.

| Raw ops/day | Batch ratio (40 ops/call) | Batch calls/day | Actions/day | Accounts | Monthly licence |
|:-----------:|:-------------------------:|:---------------:|:-----------:|:--------:|:---------------:|
| 50,000 | 40 | 1,250 | 3,750 | 1 | $51 |
| 200,000 | 40 | 5,000 | 15,000 | 1 | $51 |
| **500,000** | **40** | **12,500** | **37,500** | **1** | **$51** |
| 1,000,000 | 40 | 25,000 | 75,000 | 2 | $102 |
| 2,000,000 | 40 | 50,000 | 150,000 | 4 | $204 |
| 5,000,000 | 40 | 125,000 | 375,000 | 10 | $510 |

Batching scales extremely cost-effectively. 10× the volume = the same number of accounts
until you cross the next 40,000-actions-per-account threshold.

---

## 7. What Batching Does Not Cost

A common concern is that batching increases engineering complexity. In this solution,
`BatchOperationScript.ts` and `ExecuteBatchAsync()` are already implemented. The marginal
cost of switching from individual calls to batch calls in caller code is:

| Activity | Estimated effort |
|----------|:----------------:|
| Create BatchOperations Power Automate flow | 1–2 hours |
| Deploy `BatchOperationScript.ts` to each workbook | 10 min/workbook |
| Update caller code to use `ExecuteBatchAsync()` | 0.5–2 days per integration |
| Integration testing | 1–2 days |
| **Total one-time implementation cost** | **~3–5 days** |

At a conservative $500/day development rate, implementation cost = **$1,500–$2,500**.
This is recovered in the **first month** compared to Scenario A ($24,012/year difference).

---

## 8. Per-Flow Plan — Detailed Cost if Considered

Some teams consider the per-flow plan ($100/flow/month) because it appears to decouple
cost from user count. Here is why it is not the right choice for this solution:

| | Per-user Premium | Per-flow plan |
|--|:----------------:|:-------------:|
| Daily limit | 40,000 per user | **15,000 per flow** |
| For 37,500 actions/day (Scenario C) | 1 user = **$51/month** | 3 flows × $100 = **$300/month** |
| For 1,500,000 actions/day (Scenario A) | 38 users = $1,938/month | 100 flows = **$10,000/month** |
| Scale-out unit | +$51 per 40K actions | +$100 per 15K actions |
| Good for | **Automated machine flows** | Shared departmental flows |

**Per-flow plan costs 6× more than per-user Premium for this scenario.**
Only consider per-flow when: flows must be shared across many users and none of them
should individually hold Premium licences.

---

## 9. Azure Service Bus — When to Add It

Service Bus ($10/month flat + near-zero per-operation) pays for itself when:

| Benefit | Single replica (no SB) | Multi-replica (with SB) |
|---------|:----------------------:|:------------------------:|
| Worker restarts lose queue | Yes — in-memory Channel lost | No — messages survive restarts |
| Horizontal scaling | Not possible | Up to MaxConcurrentSessions replicas |
| Per-workbook ordering guaranteed | Within one process | Across all replicas (sessions) |
| Cost | $0 | $10/month |

**Verdict**: Add Service Bus when you deploy more than one replica. $10/month for the
guarantee that no operations are lost on worker restart is worth it.

---

## 10. Total Cost of Ownership Summary

### Recommended Production Configuration (Scenario C + Service Bus)

| Component | Purpose | Monthly | Annual |
|-----------|---------|:-------:|:------:|
| M365 E3 (1 service account) | Office Scripts licence | $36 | $432 |
| Power Automate per-user Premium (1) | HTTP trigger, premium connectors | $15 | $180 |
| Azure Container Apps (2 replicas) | Worker Service hosting | $28 | $336 |
| Azure Service Bus Standard | Distributed queue, session ordering | $10 | $120 |
| Azure Key Vault Standard | Secret management (flow URLs, SB conn string) | $1 | $12 |
| Azure Container Registry Basic | Docker image storage | $5 | $60 |
| Azure Monitor + App Insights | Logging, alerting, telemetry | $5 | $60 |
| **Total** | | **$100** | **$1,200** |

### If Scaling to 1M+ Raw Operations/Day (add 1 more account)

| Component | Monthly | Annual |
|-----------|:-------:|:------:|
| Everything above | $100 | $1,200 |
| Additional service account (M365 E3 + PA Premium) | $51 | $612 |
| Additional Container App replica | $14 | $168 |
| **Total** | **$165** | **$1,980** |

---

## 11. Decision Reference

```
Is batching implemented?
│
├─ No → Implement batching first (see office-scripts/BatchOperationScript.ts)
│        Cost: 3–5 days dev time. Saving: up to $24,516/year.
│
└─ Yes
     │
     How many batch calls/day after batching?
     │
     ├─ ≤ 13,333 → 1 account, no pool. Monthly: ~$100 all-in.
     │
     ├─ 13,334 – 26,666 → 2 accounts in FlowAccountPool. Monthly: ~$151.
     │
     ├─ 26,667 – 40,000 → 3 accounts. Monthly: ~$202.
     │
     └─ > 40,000 batch calls/day
            │
            → Are operations truly non-batchable (one independent row each)?
            │
            ├─ Yes → Account pool scales linearly at $51/13,333 batch calls/day
            │
            └─ No → Increase batch size. Review BatchOperationScript timeout headroom.
```
