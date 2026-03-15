# State Tax Modification Calculation Service

Background calculation service for the SALT (State and Local Tax) module of the enterprise corporate tax compliance platform. Handles automated and manual state modification calculations across all jurisdictions — pre-apportionment and post-apportionment — for 2,000+ clients including Fortune 500 filers.

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Solution Structure](#solution-structure)
- [Core Concepts](#core-concepts)
- [Calculation Flow](#calculation-flow)
- [Jurisdiction Strategies](#jurisdiction-strategies)
- [Configuration-Driven Rules](#configuration-driven-rules)
- [Background Worker](#background-worker)
- [Troubleshooting & Tracing](#troubleshooting--tracing)
- [Testing](#testing)
- [Adding New Jurisdictions](#adding-new-jurisdictions)
- [Adding New Tax Year Rules](#adding-new-tax-year-rules)
- [Development Setup](#development-setup)

---

## Overview

This service replaces stored-procedure-based state modification calculations with a structured, testable, and configuration-driven architecture. It handles:

- **Auto-calculated modifications** — GILTI (IRC 951A), Subpart F (IRC 951), IRC 163(j) interest add-back, IRC 168(k) bonus depreciation add-back, IRC 250 FDII/GILTI deductions, IRC 965 transition tax
- **Manual modifications** — preparer-entered overrides with full audit trail
- **Pre-apportionment** modifications (applied before the apportionment factor)
- **Post-apportionment** modifications (state NOL, DRD, applied to already-apportioned income)
- **DivCon consolidation** — aggregates member entity modifications with intercompany eliminations
- All 50 states + DC + local jurisdictions (NYC, RITA, CCA, Philadelphia, Kansas City)

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        WorkerService                                 │
│  ┌──────────────────┐      ┌─────────────────────────────────────┐  │
│  │CalculationWorker │ poll │        CalculationJobProcessor       │  │
│  │ (BackgroundSvc)  │─────▶│  resolves scope → calls Engine      │  │
│  └──────────────────┘      └─────────────────────────────────────┘  │
│         │ ICalculationJobQueue (in-memory / Service Bus)            │
└─────────┼───────────────────────────────────────────────────────────┘
          │
┌─────────▼─────────────────────────────────────────────────────────┐
│                   Application (CQRS / MediatR)                     │
│  TriggerCalculationCommand  ──▶  TriggerCalculationHandler         │
│  GetCalculationStatusQuery  ──▶  GetCalculationStatusHandler       │
│  CancelCalculationCommand   ──▶  CancelCalculationHandler          │
└────────────────────────────────────────────────────────────────────┘
          │
┌─────────▼─────────────────────────────────────────────────────────┐
│                  Calculation Engine & Pipeline                      │
│                                                                     │
│   ModificationCalculationEngine                                     │
│       │                                                             │
│       ▼  CalculationPipeline (sequential stages)                   │
│   ┌───────────────────┐                                             │
│   │ PreApportionment  │──▶ IJurisdictionModificationStrategy       │
│   │      Stage        │        └─▶ IModificationRule(s)            │
│   └───────────────────┘                                             │
│   ┌───────────────────┐                                             │
│   │  Apportionment    │──▶ IJurisdictionModificationStrategy       │
│   │      Stage        │        └─▶ IApportionmentDataProvider      │
│   └───────────────────┘                                             │
│   ┌───────────────────┐                                             │
│   │ PostApportionment │──▶ IJurisdictionModificationStrategy       │
│   │      Stage        │        └─▶ IModificationRule(s)            │
│   └───────────────────┘                                             │
│                                                                     │
│   IJurisdictionStrategyFactory                                      │
│       resolves by: JurisdictionCode + TaxYear                      │
│       "CA:2024" → "CA:2023" → "CA" → Default                      │
└────────────────────────────────────────────────────────────────────┘
          │
┌─────────▼─────────────────────────────────────────────────────────┐
│                         Domain                                      │
│   TaxEntity · FilingGroup · Jurisdiction · TaxRateSchedule         │
│   StateModification · ModificationCategory · CalculationJob        │
└────────────────────────────────────────────────────────────────────┘
          │
┌─────────▼─────────────────────────────────────────────────────────┐
│                      Infrastructure                                 │
│   SQL Server (EF Core) · InMemoryCalculationJobQueue               │
└────────────────────────────────────────────────────────────────────┘
```

### Design Patterns

| Pattern | Where Used | Why |
|---|---|---|
| **Strategy** | `IJurisdictionModificationStrategy` | Each state has unique conformity rules |
| **Pipeline** | `ICalculationStage` | Clean separation of Pre → Apportionment → Post stages |
| **Factory** | `IJurisdictionStrategyFactory` | Tax-year-aware strategy resolution |
| **Configuration-driven rules** | `RuleDefinition` / JSON | Tax law changes annually; avoid redeployment |
| **CQRS** | MediatR commands/queries | Decouple trigger from query path |
| **Repository** | `IStateModificationRepository` | Persistence abstraction for testability |
| **Audit trail** | `ModificationAuditEntry` | Every value change recorded on the entity |

---

## Solution Structure

```
StateTaxModificationCalcSvc/
│
├── src/
│   ├── Domain/
│   │   ├── Entities/               TaxEntity, FilingGroup, Jurisdiction,
│   │   │                           StateModification, ModificationCategory,
│   │   │                           CalculationJob, TaxRateSchedule, TaxBracket,
│   │   │                           ModificationAuditEntry, CalculationJobError,
│   │   │                           JurisdictionCategoryOverride, FilingGroupMembership
│   │   ├── ValueObjects/           TaxPeriod, ModificationAmount, ApportionmentFactor
│   │   ├── Enums/                  EntityType, JurisdictionCode, JurisdictionLevel,
│   │   │                           ModificationType, ModificationTiming, ModificationStatus,
│   │   │                           CalculationStatus, CalculationTrigger,
│   │   │                           ApportionmentMethod, FilingMethod, FilingGroupRole,
│   │   │                           FilingGroupType
│   │   ├── Events/                 DomainEvent, CalculationJobQueuedEvent,
│   │   │                           CalculationJobStartedEvent, CalculationJobCompletedEvent,
│   │   │                           ModificationCalculatedEvent
│   │   └── Interfaces/             IEntityRepository, IJurisdictionRepository,
│   │                               IModificationCategoryRepository,
│   │                               IStateModificationRepository, IReportLineRepository
│   │
│   ├── Calculation/
│   │   ├── Engine/                 IModificationCalculationEngine,
│   │   │                           ModificationCalculationEngine, CalculationContext,
│   │   │                           CalculationRequest, ModificationLineResult,
│   │   │                           CalculationDiagnostic, DiagnosticSeverity,
│   │   │                           CalculationTrace, DivConTrace
│   │   ├── Pipeline/               ICalculationPipeline, CalculationPipeline,
│   │   │                           ICalculationStage, PreApportionmentStage,
│   │   │                           ApportionmentStage, PostApportionmentStage
│   │   ├── Strategies/             IJurisdictionModificationStrategy,
│   │   │                           IJurisdictionStrategyFactory, JurisdictionStrategyFactory,
│   │   │                           BaseJurisdictionStrategy, DefaultJurisdictionStrategy,
│   │   │                           IApportionmentDataProvider, ApportionmentData
│   │   │   ├── States/             CaliforniaModificationStrategy,
│   │   │   │                       NewYorkModificationStrategy, IllinoisModificationStrategy
│   │   │   └── Local/              NewYorkCityModificationStrategy
│   │   └── Rules/                  IModificationRule, RuleResult,
│   │       │                       GiltiInclusionRule, GiltiDeductionRule,
│   │       │                       SubpartFInclusionRule, FdiiDeductionRule,
│   │       │                       InterestExpenseAddbackRule, BonusDepreciationAddbackRule,
│   │       │                       Section965InclusionRule
│   │       └── Config/             RuleDefinition, RuleFormulaType, RuleLineReference,
│   │                               ConfigurableModificationRule, RuleConfigurationLoader
│   │
│   ├── Application/
│   │   ├── Commands/               TriggerCalculationCommand, CancelCalculationCommand
│   │   ├── Queries/                GetCalculationStatusQuery
│   │   ├── Handlers/               TriggerCalculationHandler, GetCalculationStatusHandler,
│   │   │                           CancelCalculationHandler
│   │   └── Interfaces/             ICalculationJobQueue, ICalculationJobRepository
│   │
│   ├── Infrastructure/
│   │   ├── Messaging/              InMemoryCalculationJobQueue
│   │   └── Repositories/           ApportionmentDataRepository (stub — implement with EF Core)
│   │
│   └── WorkerService/
│       ├── Workers/                CalculationWorker (BackgroundService)
│       ├── Processors/             CalculationJobProcessor
│       ├── Config/Rules/           JSON rule definitions (see Rule Configuration below)
│       ├── ServiceCollectionExtensions.cs
│       └── Program.cs
│
└── tests/
    └── Calculation.Tests/
        ├── Builders/               CalculationContextBuilder, ModificationCategoryBuilder
        ├── Fakes/                  FakeApportionmentDataProvider, FakeJurisdictionStrategyFactory
        ├── Unit/
        │   ├── Rules/              GiltiInclusionRuleTests, ...
        │   └── Strategies/         CaliforniaStrategyTests, ...
        ├── Integration/            (wire in-memory pipeline end-to-end)
        └── Performance/            CalculationEngineBenchmarks (BenchmarkDotNet)
```

---

## Core Concepts

### Entity Types

| Code | Form | Description |
|---|---|---|
| `Form1120` | 1120 | Domestic C-Corporation |
| `Form5471` | 5471 | Controlled Foreign Corporation |
| `DisregardedEntity` | DRE | Single-member LLC treated as ignored |
| `Form8858` | 8858 | Foreign disregarded entity / branch |
| `Form1120S` | 1120-S | S-Corporation |

### Filing Groups

| Type | Description |
|---|---|
| `DivCon` | Divisional consolidation — aggregates member entities |
| `ReportingGroup` | Top-level combined reporting group |
| `Elimination` | Intercompany elimination entries |
| `Adjustment` | Manual adjustment entries |

### Modification Timing

| Timing | Description | Examples |
|---|---|---|
| `PreApportionment` | Applied to the full company amount; then the apportionment factor is applied | GILTI, Subpart F, 163(j), bonus depreciation |
| `PostApportionment` | Applied after the apportionment factor; based on in-state income | State NOL, state DRD |

---

## Calculation Flow

For each `entity × jurisdiction × tax period`:

```
1. Load entity, jurisdiction, applicable modification categories
2. Bulk-fetch all required federal report lines (one DB round-trip)

3. PreApportionmentStage
   For each pre-apportionment category:
     a. Check if excluded for this jurisdiction (via JurisdictionCategoryOverride)
     b. Resolve jurisdiction strategy (by code + tax year)
     c. Strategy dispatches to matching IModificationRule
     d. Rule reads federal report line values → computes gross amount
     e. Store in context.PreApportionmentResults

4. ApportionmentStage
   a. Strategy computes apportionment factor (sales / payroll / property per method)
   b. Apply factor to all pre-apportionment gross amounts
   c. Store apportioned amounts and combined factor in context

5. PostApportionmentStage
   For each post-apportionment category:
     a. Strategy computes amount against already-apportioned income
     b. Store in context.PostApportionmentResults

6. Persist StateModification records with full audit trail
7. Update CalculationJob progress
```

### DivCon Consolidation Flow

```
1. Trigger job scoped to a FilingGroup (DivCon type)
2. Processor iterates over all member entities in the group
3. Each entity runs the full pipeline above → individual CalculationTrace
4. EliminationCalculator applies intercompany elimination entries
5. DivConTrace aggregates all entity traces → net consolidated amount per category
6. Results written as consolidated StateModification records
```

---

## Jurisdiction Strategies

Each state has its own `IJurisdictionModificationStrategy` implementation. States with unique conformity positions are implemented explicitly; all others fall back to `DefaultJurisdictionStrategy` which uses the JSON rule configuration.

### Currently Implemented States

| State | Class | Key Differences |
|---|---|---|
| **CA** | `CaliforniaModificationStrategy` | No GILTI, no 163(j), full bonus depr add-back with CA-specific recovery, CA NOL capped at in-state income |
| **NY** | `NewYorkModificationStrategy` | 50% GILTI inclusion (2023+), NY bonus depr 5-year recovery schedule |
| **IL** | `IllinoisModificationStrategy` | No GILTI, no 163(j) conformity, IL bonus depr recovery |
| **NYC** | `NewYorkCityModificationStrategy` | Follows NY GILTI 50%, NYC receipts-factor apportionment (city limits only) |
| **All others** | `DefaultJurisdictionStrategy` | Config-driven JSON rules; no code-level override |

### Tax-Year Versioning

The factory resolves strategies using a **waterfall**:

```
GetStrategy("CA", 2024)
  1. Try key "CA:2024"  → found? return it
  2. Try key "CA:2023"  → found? return it
  3. Try key "CA:2022"  → ...
  4. Try key "CA"       → found? return it
  5. Return DefaultJurisdictionStrategy
```

Register year-specific strategies in `ServiceCollectionExtensions.cs`:

```csharp
// CA strategy valid from 2022 forward (no change expected)
services.AddKeyedTransient<IJurisdictionModificationStrategy,
    CaliforniaModificationStrategy>("CA");

// If CA law changes in 2026:
services.AddKeyedTransient<IJurisdictionModificationStrategy,
    CaliforniaModificationStrategy2026>("CA:2026");
```

---

## Configuration-Driven Rules

Rules that can be expressed as a linear formula are defined in JSON — no redeployment needed for annual rate changes.

### File Layout

```
src/WorkerService/Config/Rules/
├── default/
│   ├── gilti.json              GILTI inclusion + IRC 250 deduction
│   ├── subpart_f.json          Subpart F inclusion
│   ├── interest_163j.json      IRC 163(j) add-back
│   └── bonus_depreciation.json IRC 168(k) add-back (default)
├── states/
│   ├── NY.json                 NY GILTI 50% (2023+ override)
│   ├── CA.json                 CA-specific rules
│   └── IL.json                 IL-specific rules
└── local/
    └── NYC.json                NYC-specific rules
```

### Formula Types

| Type | Formula | Use Case |
|---|---|---|
| `LinearRate` | `SUM(lines) × rate` | GILTI inclusion (rate = 1.0, or 0.50 for NY) |
| `NetOfTwoLines` | `(line_A - line_B) × rate` | General net computations |
| `NetOfTwoLinesWithFloor` | `MAX(0, line_A - line_B) × rate` | 163(j) add-back (can't be negative) |
| `PercentageOfLine` | `line_A × rate` | FDII deduction, bonus depr |
| `LesserOf` | `MIN(line_A, line_B)` | NOL caps |
| `CodeBased` | delegates to `IModificationRule` | Complex multi-branch logic |

### Example Rule Definition

```json
{
  "ruleId": "GILTI_INCLUSION_V1",
  "categoryCode": "GILTI_INCL",
  "formulaType": "LinearRate",
  "inputLines": [
    { "lineCode": "1120_SCH_C_L10_GILTI",    "sign":  1 },
    { "lineCode": "1120_GILTI_HIGH_TAX_EXCL", "sign": -1 },
    { "lineCode": "1120_SCH_C_L10_SECT78",   "sign":  1 }
  ],
  "rate": 1.0,
  "effectiveFrom": 2018,
  "effectiveTo": null,
  "appliesToJurisdictions": ["ALL"],
  "excludedJurisdictions": ["CA", "IL"],
  "jurisdictionRateOverrides": {
    "NY": 0.50,
    "NYC": 0.50
  },
  "ircSection": "951A"
}
```

### Overriding the Rules Directory

```json
// appsettings.json
{
  "RulesDirectory": "/path/to/custom/rules"
}
```

---

## Background Worker

`CalculationWorker` is a .NET `BackgroundService` that:

1. Polls `ICalculationJobQueue` every **5 seconds**
2. Processes up to **4 concurrent jobs** (configurable via `MaxConcurrentJobs`)
3. Creates a new DI scope per job (safe for `Scoped` dependencies)
4. On graceful shutdown, waits for all in-flight jobs to complete before exiting
5. Returns failed jobs to the queue via `NackAsync` for retry

### Triggering a Calculation

```csharp
// Via MediatR
var jobId = await mediator.Send(new TriggerCalculationCommand(
    ClientId: clientId,
    TaxPeriod: TaxPeriod.CalendarYear(2024),
    Trigger: CalculationTrigger.Manual,
    RequestedBy: "preparer@firm.com",
    EntityId: null,          // null = all entities for the client
    JurisdictionId: null));  // null = all jurisdictions

// Poll status
var job = await mediator.Send(new GetCalculationStatusQuery(jobId));
Console.WriteLine($"Status: {job.Status} — {job.ProgressPercent:F0}% complete");
```

### Swapping the Queue

Replace `InMemoryCalculationJobQueue` with a production queue by implementing `ICalculationJobQueue`:

```csharp
// Azure Service Bus example
services.AddSingleton<ICalculationJobQueue, ServiceBusCalculationJobQueue>();

// Or SQL-backed queue (for simpler deployments)
services.AddScoped<ICalculationJobQueue, SqlCalculationJobQueue>();
```

---

## Troubleshooting & Tracing

### Per-Entity Trace (`CalculationTrace`)

Every calculation run accumulates a detailed trace:

```csharp
var trace = new CalculationTrace
{
    JobId = jobId,
    EntityId = entityId,
    JurisdictionId = jurisdictionId,
    TaxYear = 2024
};

// After calculation
Console.WriteLine(trace.BuildSummary());
```

Sample output:
```
=== Calculation Trace | Job: ... | Entity: ... | Jurisdiction: CA | TY: 2024 ===
    Started: 2024-03-15 10:00:00Z  Completed: 2024-03-15 10:00:01Z

  INFO | 10:00:00.001 | Stage 'PreApportionment' started
  EXCL | 10:00:00.002 | [GILTI_INCL] California does not conform to IRC 951A GILTI inclusion.
  CALC | 10:00:00.003 | [BONUS_DEPR_ADDBACK] (BONUS_DEPR_ADDBACK_DEFAULT_V1) Federal bonus depr=500,000 - CA allowed=100,000 = 400,000
  INFO | 10:00:00.004 | Stage 'Apportionment' started
  CALC | 10:00:00.005 | [BONUS_DEPR_ADDBACK] Gross=400,000 × Factor=30.0000% = Apportioned=120,000
  INFO | 10:00:00.006 | Stage 'PostApportionment' started
  CALC | 10:00:00.007 | [STATE_NOL] CA NOL available=2,000,000, Pre-NOL income=500,000, Deduction=500,000
```

### DivCon Consolidated Trace (`DivConTrace`)

```csharp
var divConTrace = new DivConTrace
{
    FilingGroupId = groupId,
    FilingGroupName = "DivCon Group A",
    TaxYear = 2024,
    JurisdictionId = jurisdictionId
};

// Add each member entity's trace
divConTrace.AddEntityTrace(entityATrace);
divConTrace.AddEntityTrace(entityBTrace);

// Add intercompany eliminations
divConTrace.AddElimination("SUBPART_F_INCL", -50_000m, "Intercompany dividend elimination");

// Net consolidated amount for a category
var netGilti = divConTrace.GetNetConsolidatedAmount("GILTI_INCL");

Console.WriteLine(divConTrace.BuildConsolidatedSummary());
```

### Diagnostics on Context

Each `CalculationContext` collects diagnostics during the run:

```csharp
// After engine.CalculateAsync(request)
foreach (var diag in context.Diagnostics)
{
    Console.WriteLine($"[{diag.Severity}] {diag.Message}");
}

if (context.HasErrors)
{
    // Job will be marked CompletedWithWarnings or Failed
}
```

---

## Testing

### Running Tests

```bash
# All unit + integration tests
dotnet test tests/Calculation.Tests

# Verbose output (shows each test name)
dotnet test tests/Calculation.Tests --logger "console;verbosity=normal"
```

**50 tests, 0 infrastructure dependencies.** All tests run in-process with no database, no DI container, no file system (except `RuleConfigurationLoader` tests which use temp directories that clean up after themselves).

### Test Coverage by Layer

| Layer | Test File | What it covers |
|---|---|---|
| Rule logic | `Unit/Rules/GiltiInclusionRuleTests.cs` | Code-based rule: net GILTI calc, year boundary, audit detail |
| Rule isolation | `Unit/Rules/ConfigurableModificationRuleTests.cs` | Year-ranged rate overrides, all formula types, `Applies()` gating |
| Config loading | `Unit/Config/RuleConfigurationLoaderTests.cs` | JSON parsing, deduplication, overlap detection, subdirectory scan |
| Strategy factory | `Unit/Strategies/JurisdictionStrategyFactoryTests.cs` | Tax-year waterfall resolution, year isolation invariants |
| State strategy | `Unit/Strategies/CaliforniaStrategyTests.cs` | CA GILTI exclusion, bonus depr net calc, NOL cap |
| Full pipeline | `Integration/ConfigDrivenPipelineTests.cs` | JSON → rules → pipeline → result; NY/MN rate change isolation |

### Test Builders and Fakes

The `tests/Calculation.Tests/Builders/` and `tests/Calculation.Tests/Fakes/` folders provide zero-infrastructure test infrastructure:

**Builders** — construct domain objects with sensible defaults; only specify what matters for the test:

```csharp
// CalculationContext
var ctx = new CalculationContextBuilder()
    .ForJurisdiction(JurisdictionCode.CA)
    .ForTaxYear(2024)
    .WithFederalLine("1120_SCH_C_L10_GILTI", 1_000_000m)
    .WithCategory(ModificationCategoryBuilder.GiltiInclusion())
    .Build();

// RuleDefinition — for testing ConfigurableModificationRule directly
var def = new RuleDefinitionBuilder()
    .WithRuleId("GILTI_V1")
    .ForCategory("GILTI_INCL")
    .WithFormula(RuleFormulaType.LinearRate)
    .WithInputLine("1120_SCH_C_L10_GILTI", sign: 1)
    .WithDefaultRate(1.0m)
    .EffectiveFrom(2018)
    .WithJurisdictionRate("NY", rate: 1.0m,  effectiveFrom: 2018, effectiveTo: 2022)
    .WithJurisdictionRate("NY", rate: 0.50m, effectiveFrom: 2023)
    .Build();
```

**Fakes** — in-memory implementations that require no setup beyond the test:

```csharp
// Apportionment data — returns configurable factor
var fakeProvider = new FakeApportionmentDataProvider()
    .WithFactor(numerator: 300_000m, denominator: 1_000_000m);

// Strategy factory — returns pre-wired strategies, throws descriptive error if miss
var fakeFactory = new FakeJurisdictionStrategyFactory()
    .WithStrategy(JurisdictionCode.CA, caStrategy)
    .WithStrategy(JurisdictionCode.NY, nyStrategy, taxYear: 2024);  // year-specific
```

### Writing a New Rule Test

```csharp
[Fact]
public async Task My_new_rule_computes_correctly_for_2025()
{
    var rule = new ConfigurableModificationRule(
        new RuleDefinitionBuilder()
            .WithRuleId("MY_RULE_V1")
            .ForCategory("MY_CATEGORY")
            .WithFormula(RuleFormulaType.LinearRate)
            .WithInputLine("MY_SOURCE_LINE", sign: 1)
            .WithDefaultRate(0.80m)
            .EffectiveFrom(2025)
            .Build());

    var ctx = new CalculationContextBuilder()
        .ForJurisdiction(JurisdictionCode.TX)
        .ForTaxYear(2025)
        .WithFederalLine("MY_SOURCE_LINE", 1_000_000m)
        .Build();

    var category = new ModificationCategoryBuilder()
        .WithCode("MY_CATEGORY")
        .Build();

    var result = await rule.ComputeAsync(ctx, category);

    result.Amount.Should().Be(800_000m);
}
```

### Writing a Year-Isolation Test

Verify that adding a new rate for year N does not touch year N-1 or N+1:

```csharp
[Fact]
public async Task New_rate_for_2026_does_not_affect_2025()
{
    var def = new RuleDefinitionBuilder()
        ...
        .WithJurisdictionRate("TX", rate: 0.50m, effectiveFrom: 2025, effectiveTo: 2025)
        .WithJurisdictionRate("TX", rate: 0.75m, effectiveFrom: 2026)
        .Build();

    // Assert 2025 result, 2026 result, 2024 falls back to default
}
```

### Integration Tests (Config-Driven)

`Integration/ConfigDrivenPipelineTests.cs` writes JSON rule definitions to a temp directory, loads them via `RuleConfigurationLoader`, wires a full pipeline, and asserts end-to-end results. This is the closest test to production behavior without a database.

### Performance Benchmarks

Benchmarks are in their own project (`tests/Calculation.Benchmarks`) and must be run in Release configuration — they cannot be run with `dotnet test`:

```bash
dotnet run -c Release --project tests/Calculation.Benchmarks -- --filter "*Benchmarks*"
```

Benchmarks measure:
- Single entity full CA pipeline throughput (end-to-end latency)
- Strategy factory resolution overhead (1,000 lookups)

---

## Adding New Jurisdictions

### Simple State (JSON-only, no unique conformity)

1. Add state-specific rule overrides in `src/WorkerService/Config/Rules/states/TX.json`
2. No code changes needed — `DefaultJurisdictionStrategy` handles it

### Complex State (unique conformity rules)

1. Create `src/Calculation/Strategies/States/TexasModificationStrategy.cs`:

```csharp
public sealed class TexasModificationStrategy : BaseJurisdictionStrategy
{
    public TexasModificationStrategy(
        IEnumerable<IModificationRule> rules,
        IApportionmentDataProvider apportionmentData,
        ILogger<TexasModificationStrategy> logger)
        : base(rules, apportionmentData, logger) { }

    public override async Task<ModificationLineResult> ComputePreApportionmentAsync(
        CalculationContext context, ModificationCategory category, CancellationToken ct = default)
    {
        // TX-specific: no 163(j), no GILTI
        if (category.DefaultModificationType is
            ModificationType.InterestExpenseAddback or ModificationType.GiltiInclusion)
        {
            return new ModificationLineResult
            {
                CategoryId = category.Id, Category = category,
                IsExcluded = true,
                ExclusionReason = "Texas does not conform."
            };
        }
        return await base.ComputePreApportionmentAsync(context, category, ct);
    }
}
```

2. Register in `ServiceCollectionExtensions.cs`:

```csharp
services.AddKeyedTransient<IJurisdictionModificationStrategy,
    TexasModificationStrategy>("TX");
```

### Local Jurisdiction

Same as above but place in `src/Calculation/Strategies/Local/` and register with the local jurisdiction code (e.g., `"RITA"`, `"CCA"`).

---

## Adding New Tax Year Rules

### Design Invariant: Year Changes Must Be Isolated

A rate or rule change for tax year N must **never** affect calculations for year N-1 or N+1. The system enforces this through two mechanisms:

1. `effectiveFrom` / `effectiveTo` on each `RuleDefinition` — gates the entire rule to a year range
2. `JurisdictionRateOverrides` — per-jurisdiction rate history with year ranges, so a state rate change in 2025 does not touch 2024 data

At startup, `RuleConfigurationLoader` logs a `Warning` if two rules with different `RuleId`s target the same category/jurisdiction/year overlap, so configuration mistakes are caught before the first calculation runs.

### When a Jurisdiction's Rate Changes (JSON only — most common)

Add a new range to `JurisdictionRateOverrides` on the existing rule. Do **not** create a new `RuleId` — that causes a double-match risk:

```json
// default/gilti.json — add NY range for 2025 change
"jurisdictionRateOverrides": {
  "NY": [
    { "rate": 1.0,  "effectiveFrom": 2018, "effectiveTo": 2022 },
    { "rate": 0.50, "effectiveFrom": 2023, "effectiveTo": 2024, "changeNote": "Budget Part CC" },
    { "rate": 0.40, "effectiveFrom": 2025,                       "changeNote": "2024 session law" }
  ]
}
```

This is the only change needed. The year 2024 continues to resolve 0.50; 2025 resolves 0.40; 2022 resolves 1.0.

### When a Universal Rate Changes for All Jurisdictions (new RuleId + year boundary)

Use this when the federal law itself changes (e.g. TCJA sunset applies everywhere). Create a new rule with a non-overlapping year range and expire the old one:

```json
// gilti.json — expire old rule
{ "ruleId": "GILTI_DEDUCTION_V1", ..., "effectiveTo": 2025 },

// new rule — starts immediately where V1 ends, zero year overlap
{
  "ruleId": "GILTI_DEDUCTION_V2",
  "categoryCode": "GILTI_DEDUCT",
  "formulaType": "PercentageOfLine",
  "inputLines": [{ "lineCode": "1120_SCH_C_L36_SECT250_GILTI", "sign": 1 }],
  "rate": -0.375,
  "effectiveFrom": 2026,
  "effectiveTo": null,
  "ircSection": "250(a)(1)(B)",
  "jurisdictionRateOverrides": {},
  "changeNotes": "TCJA sunset — deduction rate reduced from 50% to 37.5%"
}
```

`V1.effectiveTo=2025` and `V2.effectiveFrom=2026` guarantee zero overlap. The engine picks exactly one rule per year with no overlap detection warning.

### When a State Changes Conformity Structurally (code change)

Use this when the formula itself changes (different input lines, different formula type, state-specific carry-forward logic). For rate-only changes, use `JurisdictionRateOverrides` above instead.

1. Register a year-specific strategy: `services.AddKeyedTransient<..., NewYorkModificationStrategy>("NY:2026")`
2. Implement the new year's logic in the strategy class
3. Write a year-isolation test confirming NY 2025 still resolves to the prior strategy

---

## Development Setup

### Prerequisites

- .NET 9 SDK
- SQL Server (or LocalDB for development)

### Build

```bash
dotnet build StateTaxModificationCalcSvc.slnx
```

### Run Tests

```bash
dotnet test StateTaxModificationCalcSvc.slnx
```

### Run Worker Service

```bash
dotnet run --project src/WorkerService
```

### Configuration

`src/WorkerService/appsettings.json`:

```json
{
  "RulesDirectory": "Config/Rules",
  "ConnectionStrings": {
    "TaxDb": "Server=.;Database=TaxCompliance;Trusted_Connection=true;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Calculation": "Debug"
    }
  }
}
```

### Next Steps (Infrastructure Stubs to Implement)

The following repository stubs in `Infrastructure/Repositories/` need EF Core implementations before the service is production-ready:

| Interface | Stub | Priority |
|---|---|---|
| `IEntityRepository` | _(not yet created)_ | High |
| `IJurisdictionRepository` | _(not yet created)_ | High |
| `IModificationCategoryRepository` | _(not yet created)_ | High |
| `IStateModificationRepository` | _(not yet created)_ | High |
| `IReportLineRepository` | _(not yet created)_ | High |
| `ICalculationJobRepository` | _(not yet created)_ | High |
| `IApportionmentDataProvider` | `ApportionmentDataRepository` (stub) | High |

---

## Federal Report Line Codes

Standard line code format: `{FORM}_{SCHEDULE}_{LINE}_{DESCRIPTION}`

| Code | Source | Used By |
|---|---|---|
| `1120_SCH_C_L10_GILTI` | Form 1120, Schedule C, Line 10 | GILTI inclusion |
| `1120_GILTI_HIGH_TAX_EXCL` | Form 8992 / election | GILTI high-tax exclusion |
| `1120_SCH_C_L10_SECT78` | Form 1120, Schedule C, Line 10 | §78 GILTI gross-up |
| `1120_SCH_C_L36_SECT250_GILTI` | Form 1120, Schedule C, Line 36 | §250 GILTI deduction |
| `1120_SCH_C_L36_SECT250_FDII` | Form 1120, Schedule C, Line 36 | §250 FDII deduction |
| `1120_SCH_C_L1_SUBPART_F` | Form 1120, Schedule C, Line 1 | Subpart F income |
| `1120_SCH_C_L9_FOREIGN_DIV` | Form 1120, Schedule C, Line 9 | PTI distributions |
| `1120_SCH_C_L6_SECT965` | Form 1120, Schedule C, Line 6 | §965 inclusion |
| `1120_SCH_C_L7_SECT965_DEDUCT` | Form 1120, Schedule C, Line 7 | §965(c) deduction |
| `1120_F8990_L30_DISALLOWED_INT` | Form 8990, Line 30 | §163(j) disallowed interest |
| `1120_F8990_L6_PRIOR_CARRYOVER` | Form 8990, Line 6 | §163(j) prior carryover |
| `1120_M3_L30_BONUS_DEPR` | Schedule M-3, Part III, Line 30 | §168(k) bonus depreciation |
| `CA_ALLOWED_DEPR` | CA state return | CA-allowed depreciation (basis for CA add-back) |
| `CA_NOL_AVAILABLE_BALANCE` | CA NOL schedule | CA NOL carryforward balance |
| `NY_BONUS_DEPR_RECOVERY` | NY state return | NY 5-year bonus depr recovery |
| `IL_BONUS_DEPR_RECOVERY` | IL state return | IL bonus depr recovery |
