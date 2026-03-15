using Calculation.Rules.Config;
using Calculation.Tests.Builders;
using Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Calculation.Tests.Unit.Rules;

/// <summary>
/// Tests for ConfigurableModificationRule — covering year isolation (the primary
/// concern: a rate change for 2024 must not affect 2023 or 2025), rate range
/// resolution, formula types, and Applies() gating.
/// </summary>
public sealed class ConfigurableModificationRuleTests
{
    // ── Year isolation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Rate_change_for_2024_does_not_affect_2023()
    {
        // NY had 100% inclusion through 2022; 50% from 2023.
        // Requesting 2022 must use 1.0, not 0.50.
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .WithDefaultRate(1.0m)
                .EffectiveFrom(2018)
                .WithJurisdictionRate("NY", rate: 1.0m,  effectiveFrom: 2018, effectiveTo: 2022)
                .WithJurisdictionRate("NY", rate: 0.50m, effectiveFrom: 2023)
                .Build());

        var ctx2022 = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(2022)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var ctx2023 = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(2023)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var category = ModificationCategoryBuilder.GiltiInclusion();

        var result2022 = await rule.ComputeAsync(ctx2022, category);
        var result2023 = await rule.ComputeAsync(ctx2023, category);

        result2022.Amount.Should().Be(1_000_000m, "2022 uses 100% rate — pre-budget");
        result2023.Amount.Should().Be(500_000m,   "2023 uses 50% rate — Part CC budget");
    }

    [Fact]
    public async Task Rate_change_in_2024_does_not_affect_2025()
    {
        // A range capped at 2024 should not bleed into 2025;
        // the next range (or default) must take over cleanly.
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .WithDefaultRate(1.0m)
                .EffectiveFrom(2018)
                .WithJurisdictionRate("MN", rate: 1.0m,  effectiveFrom: 2018, effectiveTo: 2023)
                .WithJurisdictionRate("MN", rate: 0.50m, effectiveFrom: 2024)
                .Build());

        var ctx2023 = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.MN)
            .ForTaxYear(2023)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var ctx2024 = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.MN)
            .ForTaxYear(2024)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var ctx2025 = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.MN)
            .ForTaxYear(2025)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var category = ModificationCategoryBuilder.GiltiInclusion();

        var result2023 = await rule.ComputeAsync(ctx2023, category);
        var result2024 = await rule.ComputeAsync(ctx2024, category);
        var result2025 = await rule.ComputeAsync(ctx2025, category);

        result2023.Amount.Should().Be(1_000_000m, "2023 still uses 100% rate");
        result2024.Amount.Should().Be(500_000m,   "2024 uses 50% rate");
        result2025.Amount.Should().Be(500_000m,   "2025 continues 50% — no new range, most-recent range applies");
    }

    [Fact]
    public async Task Falls_back_to_default_rate_when_jurisdiction_has_no_matching_range()
    {
        // NY override starts 2023 — querying NY for 2020 should fall back to default 1.0m.
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .WithDefaultRate(1.0m)
                .EffectiveFrom(2018)
                .WithJurisdictionRate("NY", rate: 0.50m, effectiveFrom: 2023)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(2020)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var result = await rule.ComputeAsync(ctx, ModificationCategoryBuilder.GiltiInclusion());

        result.Amount.Should().Be(1_000_000m,
            "No NY range covers 2020 — falls back to default rate 1.0");
    }

    [Fact]
    public async Task Falls_back_to_default_rate_for_jurisdiction_with_no_overrides()
    {
        // CT has no overrides — should use default rate.
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .WithDefaultRate(0.75m) // non-trivial default
                .EffectiveFrom(2018)
                .WithJurisdictionRate("NY", rate: 0.50m, effectiveFrom: 2023)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CT)
            .ForTaxYear(2024)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var result = await rule.ComputeAsync(ctx, ModificationCategoryBuilder.GiltiInclusion());

        result.Amount.Should().Be(750_000m, "CT has no override — uses default rate 0.75");
    }

    [Fact]
    public async Task Most_recent_applicable_range_wins_when_ranges_are_open_ended()
    {
        // Two open-ended ranges would be a config error, but if present the
        // higher EffectiveFrom (most-recent) should win for any year ≥ its start.
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .WithDefaultRate(1.0m)
                .EffectiveFrom(2018)
                .WithJurisdictionRate("NJ", rate: 0.75m, effectiveFrom: 2018)
                .WithJurisdictionRate("NJ", rate: 0.50m, effectiveFrom: 2022)
                .Build());

        var ctx2021 = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NJ)
            .ForTaxYear(2021)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var ctx2024 = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NJ)
            .ForTaxYear(2024)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var category = ModificationCategoryBuilder.GiltiInclusion();

        var result2021 = await rule.ComputeAsync(ctx2021, category);
        var result2024 = await rule.ComputeAsync(ctx2024, category);

        result2021.Amount.Should().Be(750_000m, "2021 is only covered by the 2018 range");
        result2024.Amount.Should().Be(500_000m, "2024 is covered by both, most-recent (2022) wins");
    }

    // ── Applies() gating ───────────────────────────────────────────────────

    [Theory]
    [InlineData(2016)] // before effectiveFrom=2018
    [InlineData(2017)]
    public void Applies_returns_false_before_rule_effective_year(int taxYear)
    {
        var rule = new ConfigurableModificationRule(RuleDefinitionBuilder.GiltiInclusion());
        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(taxYear)
            .Build();

        rule.Applies(ModificationCategoryBuilder.GiltiInclusion(), ctx).Should().BeFalse();
    }

    [Fact]
    public void Applies_returns_false_after_effectiveTo()
    {
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .EffectiveFrom(2018)
                .EffectiveTo(2025)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(2026)
            .Build();

        rule.Applies(ModificationCategoryBuilder.GiltiInclusion(), ctx).Should().BeFalse();
    }

    [Fact]
    public void Applies_returns_false_for_excluded_jurisdiction()
    {
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .EffectiveFrom(2018)
                .ExcludeJurisdiction("CA")
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .Build();

        rule.Applies(ModificationCategoryBuilder.GiltiInclusion(), ctx).Should().BeFalse();
    }

    [Fact]
    public void Applies_returns_false_when_category_code_does_not_match()
    {
        var rule = new ConfigurableModificationRule(RuleDefinitionBuilder.GiltiInclusion());
        var ctx = new CalculationContextBuilder().ForTaxYear(2024).Build();

        // BONUS_DEPR_ADDBACK != GILTI_INCL
        rule.Applies(ModificationCategoryBuilder.BonusDepreciationAddback(), ctx).Should().BeFalse();
    }

    [Fact]
    public void Applies_returns_false_when_jurisdiction_not_in_appliesToJurisdictions()
    {
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .EffectiveFrom(2018)
                .ForJurisdictions("NY", "CT")  // only NY and CT
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .Build();

        rule.Applies(ModificationCategoryBuilder.GiltiInclusion(), ctx).Should().BeFalse();
    }

    // ── Formula types ──────────────────────────────────────────────────────

    [Fact]
    public async Task LinearRate_sums_signed_lines_then_multiplies_by_rate()
    {
        // GILTI net = 1,000,000 (gross) - 100,000 (excl) + 50,000 (§78) = 950,000
        // Rate = 0.50 for NY → 475,000
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("1120_SCH_C_L10_GILTI",    sign:  1)
                .WithInputLine("1120_GILTI_HIGH_TAX_EXCL", sign: -1)
                .WithInputLine("1120_SCH_C_L10_SECT78",    sign:  1)
                .WithDefaultRate(0.50m)
                .EffectiveFrom(2018)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(2024)
            .WithFederalLine("1120_SCH_C_L10_GILTI",    1_000_000m)
            .WithFederalLine("1120_GILTI_HIGH_TAX_EXCL",  100_000m)
            .WithFederalLine("1120_SCH_C_L10_SECT78",       50_000m)
            .Build();

        var result = await rule.ComputeAsync(ctx, ModificationCategoryBuilder.GiltiInclusion());

        // (1,000,000 - 100,000 + 50,000) * 0.50 = 475,000
        result.Amount.Should().Be(475_000m);
    }

    [Fact]
    public async Task LinearRate_applies_MaximumAmount_cap()
    {
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .WithDefaultRate(1.0m)
                .WithMaxAmount(300_000m)
                .EffectiveFrom(2018)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(2024)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var result = await rule.ComputeAsync(ctx, ModificationCategoryBuilder.GiltiInclusion());

        result.Amount.Should().Be(300_000m, "capped at MaximumAmount");
    }

    [Fact]
    public async Task NetOfTwoLines_computes_first_minus_second_times_rate()
    {
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("BONUS_DEPR_ADDBACK")
                .WithFormula(RuleFormulaType.NetOfTwoLines)
                .WithInputLine("FEDERAL_BONUS", sign: 1)
                .WithInputLine("STATE_ALLOWED",  sign: 1)
                .WithDefaultRate(1.0m)
                .EffectiveFrom(2002)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .WithFederalLine("FEDERAL_BONUS", 500_000m)
            .WithFederalLine("STATE_ALLOWED",  100_000m)
            .Build();

        var result = await rule.ComputeAsync(ctx, ModificationCategoryBuilder.BonusDepreciationAddback());

        result.Amount.Should().Be(400_000m); // 500k - 100k
    }

    [Fact]
    public async Task NetOfTwoLinesWithFloor_floors_negative_result_at_zero()
    {
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("BONUS_DEPR_ADDBACK")
                .WithFormula(RuleFormulaType.NetOfTwoLinesWithFloor)
                .WithInputLine("FEDERAL_BONUS", sign: 1)
                .WithInputLine("STATE_ALLOWED",  sign: 1)
                .WithDefaultRate(1.0m)
                .EffectiveFrom(2002)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .WithFederalLine("FEDERAL_BONUS", 100_000m)
            .WithFederalLine("STATE_ALLOWED", 200_000m) // CA allows MORE than federal
            .Build();

        var result = await rule.ComputeAsync(ctx, ModificationCategoryBuilder.BonusDepreciationAddback());

        result.Amount.Should().Be(0m, "floored at zero — CA allowing more than federal means no add-back");
    }

    [Fact]
    public async Task PercentageOfLine_multiplies_single_line_by_rate()
    {
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.PercentageOfLine)
                .WithInputLine("LINE_A", sign: 1)
                .WithDefaultRate(-0.50m) // deduction = negative
                .EffectiveFrom(2018)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(2024)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var result = await rule.ComputeAsync(ctx, ModificationCategoryBuilder.GiltiInclusion());

        result.Amount.Should().Be(-500_000m);
    }

    [Fact]
    public async Task LesserOf_selects_the_smaller_of_two_lines()
    {
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("TEST")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LesserOf)
                .WithInputLine("LINE_A", sign: 1)
                .WithInputLine("LINE_B", sign: 1)
                .WithDefaultRate(1.0m)
                .EffectiveFrom(2018)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(2024)
            .WithFederalLine("LINE_A", 800_000m)
            .WithFederalLine("LINE_B", 500_000m)
            .Build();

        var result = await rule.ComputeAsync(ctx, ModificationCategoryBuilder.GiltiInclusion());

        result.Amount.Should().Be(500_000m);
    }

    // ── Audit detail ───────────────────────────────────────────────────────

    [Fact]
    public async Task Compute_detail_contains_ruleId_for_audit_trail()
    {
        var rule = new ConfigurableModificationRule(
            new RuleDefinitionBuilder()
                .WithRuleId("GILTI_INCLUSION_V1")
                .ForCategory("GILTI_INCL")
                .WithFormula(RuleFormulaType.LinearRate)
                .WithInputLine("LINE_A", sign: 1)
                .EffectiveFrom(2018)
                .Build());

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.NY)
            .ForTaxYear(2024)
            .WithFederalLine("LINE_A", 1_000_000m)
            .Build();

        var result = await rule.ComputeAsync(ctx, ModificationCategoryBuilder.GiltiInclusion());

        result.Detail.Should().Contain("GILTI_INCLUSION_V1");
    }
}
