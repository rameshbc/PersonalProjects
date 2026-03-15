using Calculation.Engine;
using Calculation.Pipeline;
using Calculation.Rules;
using Calculation.Rules.Config;
using Calculation.Strategies;
using Calculation.Strategies.States;
using Calculation.Tests.Builders;
using Calculation.Tests.Fakes;
using Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Calculation.Tests.Integration;

/// <summary>
/// End-to-end integration tests that load RuleDefinitions from inline JSON,
/// build ConfigurableModificationRules from them, wire into a full pipeline,
/// and assert results.
///
/// These tests verify that year-isolated rate changes propagate correctly
/// through the entire calculation stack — not just at the rule level.
/// </summary>
public sealed class ConfigDrivenPipelineTests : IDisposable
{
    private readonly string _rulesDir;
    private readonly RuleConfigurationLoader _loader = new(NullLogger<RuleConfigurationLoader>.Instance);

    public ConfigDrivenPipelineTests()
    {
        _rulesDir = Path.Combine(Path.GetTempPath(), $"integration_rules_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rulesDir);
    }

    public void Dispose() => Directory.Delete(_rulesDir, recursive: true);

    // ── Year isolation through full pipeline ───────────────────────────────

    [Fact]
    public async Task NY_GILTI_rate_change_in_2023_is_isolated_from_2022()
    {
        // Write a GILTI rule with NY year-ranged override — mirrors production config.
        WriteJson("gilti.json", """
            [{
              "ruleId": "GILTI_INCLUSION_V1",
              "categoryCode": "GILTI_INCL",
              "formulaType": "LinearRate",
              "inputLines": [
                { "lineCode": "1120_SCH_C_L10_GILTI",     "sign":  1 },
                { "lineCode": "1120_GILTI_HIGH_TAX_EXCL",  "sign": -1 },
                { "lineCode": "1120_SCH_C_L10_SECT78",     "sign":  1 }
              ],
              "rate": 1.0,
              "effectiveFrom": 2018,
              "appliesToJurisdictions": ["ALL"],
              "excludedJurisdictions": ["CA", "IL"],
              "jurisdictionRateOverrides": {
                "NY": [
                  { "rate": 1.0,  "effectiveFrom": 2018, "effectiveTo": 2022 },
                  { "rate": 0.50, "effectiveFrom": 2023, "changeNote": "Budget Part CC" }
                ]
              }
            }]
            """);

        var giltiCategory = ModificationCategoryBuilder.GiltiInclusion();

        var result2022 = await RunSingleCategoryPipeline(
            jurisdictionCode: JurisdictionCode.NY,
            taxYear: 2022,
            category: giltiCategory,
            federalLines: new()
            {
                ["1120_SCH_C_L10_GILTI"]    = 1_000_000m,
                ["1120_GILTI_HIGH_TAX_EXCL"] = 0m,
                ["1120_SCH_C_L10_SECT78"]    = 0m
            });

        var result2023 = await RunSingleCategoryPipeline(
            jurisdictionCode: JurisdictionCode.NY,
            taxYear: 2023,
            category: giltiCategory,
            federalLines: new()
            {
                ["1120_SCH_C_L10_GILTI"]    = 1_000_000m,
                ["1120_GILTI_HIGH_TAX_EXCL"] = 0m,
                ["1120_SCH_C_L10_SECT78"]    = 0m
            });

        result2022.GrossAmount.Should().Be(1_000_000m,
            "NY 2022 uses 100% rate — pre-budget, the 2023 change must not affect this");

        result2023.GrossAmount.Should().Be(500_000m,
            "NY 2023 uses 50% rate — Part CC budget conformity");
    }

    [Fact]
    public async Task MN_rate_change_in_2024_does_not_affect_2023_or_2025()
    {
        WriteJson("gilti.json", """
            [{
              "ruleId": "GILTI_INCLUSION_V1",
              "categoryCode": "GILTI_INCL",
              "formulaType": "LinearRate",
              "inputLines": [
                { "lineCode": "1120_SCH_C_L10_GILTI", "sign": 1 }
              ],
              "rate": 1.0,
              "effectiveFrom": 2018,
              "appliesToJurisdictions": ["ALL"],
              "excludedJurisdictions": ["CA", "IL"],
              "jurisdictionRateOverrides": {
                "MN": [
                  { "rate": 1.0,  "effectiveFrom": 2018, "effectiveTo": 2023 },
                  { "rate": 0.50, "effectiveFrom": 2024, "changeNote": "2023 Session Law Ch.64" }
                ]
              }
            }]
            """);

        var category = ModificationCategoryBuilder.GiltiInclusion();
        var federalLines = new Dictionary<string, decimal?> { ["1120_SCH_C_L10_GILTI"] = 1_000_000m };

        var r2023 = await RunSingleCategoryPipeline(JurisdictionCode.MN, 2023, category, federalLines);
        var r2024 = await RunSingleCategoryPipeline(JurisdictionCode.MN, 2024, category, federalLines);
        var r2025 = await RunSingleCategoryPipeline(JurisdictionCode.MN, 2025, category, federalLines);

        r2023.GrossAmount.Should().Be(1_000_000m, "2023: 100% rate, before change");
        r2024.GrossAmount.Should().Be(500_000m,   "2024: 50% rate, change takes effect");
        r2025.GrossAmount.Should().Be(500_000m,   "2025: still 50% — most recent applicable range");
    }

    [Fact]
    public async Task Excluded_jurisdiction_produces_no_result_regardless_of_tax_year()
    {
        WriteJson("gilti.json", """
            [{
              "ruleId": "GILTI_INCLUSION_V1",
              "categoryCode": "GILTI_INCL",
              "formulaType": "LinearRate",
              "inputLines": [{ "lineCode": "1120_SCH_C_L10_GILTI", "sign": 1 }],
              "rate": 1.0,
              "effectiveFrom": 2018,
              "appliesToJurisdictions": ["ALL"],
              "excludedJurisdictions": ["CA"],
              "jurisdictionRateOverrides": {}
            }]
            """);

        var category = ModificationCategoryBuilder.GiltiInclusion();
        var federalLines = new Dictionary<string, decimal?> { ["1120_SCH_C_L10_GILTI"] = 1_000_000m };

        // CA excluded — no rule fires — category-level exclusion from the strategy handles it.
        // The pipeline produces a result with GrossAmount=0 and IsExcluded=true for CA.
        var ctx2024 = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .WithFederalLines(federalLines)
            .WithCategory(category)
            .Build();

        var rules = LoadRules();
        var pipeline = BuildPipeline(JurisdictionCode.CA, rules);
        await pipeline.ExecuteAsync(ctx2024);

        // CA strategy handles GILTI exclusion — no pre-apportionment result for GILTI.
        // If the configurable rule is excluded (CA in ExcludedJurisdictions), Applies() returns false,
        // meaning no rule computes a value and GrossAmount stays at its default (0).
        ctx2024.PreApportionmentResults.Values
            .Where(r => r.Category?.Code == "GILTI_INCL")
            .Should().AllSatisfy(r => r.GrossAmount.Should().Be(0m));
    }

    [Fact]
    public async Task GILTI_deduction_v1_expires_at_2025_and_does_not_apply_2026()
    {
        // Two rules — V1 ends 2025, V2 starts 2026 — clean handoff with zero overlap.
        WriteJson("gilti_deduct.json", """
            [
              {
                "ruleId": "GILTI_DEDUCTION_V1",
                "categoryCode": "GILTI_DEDUCT",
                "formulaType": "PercentageOfLine",
                "inputLines": [{ "lineCode": "GILTI_DEDUCT_LINE", "sign": 1 }],
                "rate": -1.0,
                "effectiveFrom": 2018,
                "effectiveTo": 2025,
                "appliesToJurisdictions": ["ALL"],
                "excludedJurisdictions": [],
                "jurisdictionRateOverrides": {}
              },
              {
                "ruleId": "GILTI_DEDUCTION_V2",
                "categoryCode": "GILTI_DEDUCT",
                "formulaType": "PercentageOfLine",
                "inputLines": [{ "lineCode": "GILTI_DEDUCT_LINE", "sign": 1 }],
                "rate": -0.375,
                "effectiveFrom": 2026,
                "appliesToJurisdictions": ["ALL"],
                "excludedJurisdictions": [],
                "jurisdictionRateOverrides": {}
              }
            ]
            """);

        var deductionCategory = new ModificationCategoryBuilder()
            .WithCode("GILTI_DEDUCT")
            .WithDescription("GILTI Deduction IRC 250")
            .WithType(Domain.Enums.ModificationType.GiltiDeduction)
            .WithTiming(Domain.Enums.ModificationTiming.PreApportionment)
            .Build();

        var federalLines = new Dictionary<string, decimal?> { ["GILTI_DEDUCT_LINE"] = 1_000_000m };

        var r2025 = await RunSingleCategoryPipeline(JurisdictionCode.NY, 2025, deductionCategory, federalLines);
        var r2026 = await RunSingleCategoryPipeline(JurisdictionCode.NY, 2026, deductionCategory, federalLines);

        r2025.GrossAmount.Should().Be(-1_000_000m, "V1: 100% deduction through 2025");
        r2026.GrossAmount.Should().Be(-375_000m,   "V2: 37.5% post-TCJA sunset from 2026");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void WriteJson(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_rulesDir, fileName), json);

    private IReadOnlyList<RuleDefinition> LoadRules() =>
        _loader.LoadAll(_rulesDir);

    private IEnumerable<IModificationRule> BuildConfigurableRules(IReadOnlyList<RuleDefinition> defs) =>
        defs.Select(d => (IModificationRule)new ConfigurableModificationRule(d));

    private CalculationPipeline BuildPipeline(JurisdictionCode code, IReadOnlyList<RuleDefinition> defs)
    {
        var rules = BuildConfigurableRules(defs).ToArray();
        var apportionmentProvider = new FakeApportionmentDataProvider()
            .WithFactor(numerator: 300_000m, denominator: 1_000_000m);

        IJurisdictionModificationStrategy strategy = code switch
        {
            JurisdictionCode.CA => new CaliforniaModificationStrategy(
                rules, apportionmentProvider,
                NullLogger<CaliforniaModificationStrategy>.Instance),
            _ => new DefaultStrategyForTest(rules, apportionmentProvider)
        };

        var factory = new FakeJurisdictionStrategyFactory()
            .WithStrategy(code, strategy);

        return new CalculationPipeline(
            [
                new PreApportionmentStage(factory,  NullLogger<PreApportionmentStage>.Instance),
                new ApportionmentStage(factory,     NullLogger<ApportionmentStage>.Instance),
                new PostApportionmentStage(factory, NullLogger<PostApportionmentStage>.Instance)
            ],
            NullLogger<CalculationPipeline>.Instance);
    }

    private async Task<ModificationLineResult> RunSingleCategoryPipeline(
        JurisdictionCode jurisdictionCode,
        int taxYear,
        Domain.Entities.ModificationCategory category,
        Dictionary<string, decimal?> federalLines)
    {
        var rules = LoadRules();
        var pipeline = BuildPipeline(jurisdictionCode, rules);

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(jurisdictionCode)
            .ForTaxYear(taxYear)
            .WithFederalLines(federalLines)
            .WithCategory(category)
            .Build();

        await pipeline.ExecuteAsync(ctx);

        // Return the first pre-apportionment result for the category (or empty result if none fired).
        return ctx.PreApportionmentResults.Values
            .FirstOrDefault(r => r.Category?.Code == category.Code)
            ?? new ModificationLineResult();
    }
}

// ── Local helpers ──────────────────────────────────────────────────────────

/// <summary>
/// Minimal pass-through strategy that applies rules without CA/NY-specific logic.
/// Used in integration tests for non-CA jurisdictions.
/// </summary>
internal sealed class DefaultStrategyForTest : Calculation.Strategies.BaseJurisdictionStrategy
{
    public DefaultStrategyForTest(
        IEnumerable<IModificationRule> rules,
        IApportionmentDataProvider apportionmentData)
        : base(rules, apportionmentData,
               NullLogger<DefaultStrategyForTest>.Instance) { }
}
