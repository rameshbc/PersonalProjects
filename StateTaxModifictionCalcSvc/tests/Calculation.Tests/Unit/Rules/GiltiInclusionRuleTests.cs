using Calculation.Rules;
using Calculation.Tests.Builders;
using Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Calculation.Tests.Unit.Rules;

public sealed class GiltiInclusionRuleTests
{
    private readonly GiltiInclusionRule _rule = new();

    [Fact]
    public async Task Computes_net_GILTI_as_gross_minus_high_tax_exclusion_plus_section78()
    {
        var ctx = new CalculationContextBuilder()
            .ForTaxYear(2024)
            .WithFederalLine("1120_SCH_C_L10_GILTI", 1_000_000m)
            .WithFederalLine("1120_GILTI_HIGH_TAX_EXCL", 100_000m)
            .WithFederalLine("1120_SCH_C_L10_SECT78", 50_000m)
            .Build();

        var category = ModificationCategoryBuilder.GiltiInclusion();
        var result = await _rule.ComputeAsync(ctx, category);

        // Net = 1,000,000 - 100,000 + 50,000 = 950,000
        result.Amount.Should().Be(950_000m);
    }

    [Fact]
    public async Task Returns_zero_when_all_GILTI_excluded_via_high_tax()
    {
        var ctx = new CalculationContextBuilder()
            .ForTaxYear(2024)
            .WithFederalLine("1120_SCH_C_L10_GILTI", 500_000m)
            .WithFederalLine("1120_GILTI_HIGH_TAX_EXCL", 500_000m)
            .WithFederalLine("1120_SCH_C_L10_SECT78", 0m)
            .Build();

        var category = ModificationCategoryBuilder.GiltiInclusion();
        var result = await _rule.ComputeAsync(ctx, category);

        result.Amount.Should().Be(0m);
    }

    [Theory]
    [InlineData(2017)] // Pre-TCJA — rule should not apply
    [InlineData(2016)]
    public void Does_not_apply_before_2018(int taxYear)
    {
        var ctx = new CalculationContextBuilder().ForTaxYear(taxYear).Build();
        var category = ModificationCategoryBuilder.GiltiInclusion();

        _rule.Applies(category, ctx).Should().BeFalse();
    }

    [Theory]
    [InlineData(2018)]
    [InlineData(2024)]
    [InlineData(2025)]
    public void Applies_from_2018_onwards(int taxYear)
    {
        var ctx = new CalculationContextBuilder().ForTaxYear(taxYear).Build();
        var category = ModificationCategoryBuilder.GiltiInclusion();

        _rule.Applies(category, ctx).Should().BeTrue();
    }

    [Fact]
    public async Task Result_contains_rule_id_in_detail_for_audit()
    {
        var ctx = new CalculationContextBuilder()
            .ForTaxYear(2024)
            .WithFederalLine("1120_SCH_C_L10_GILTI", 100m)
            .WithFederalLine("1120_GILTI_HIGH_TAX_EXCL", 0m)
            .WithFederalLine("1120_SCH_C_L10_SECT78", 0m)
            .Build();

        var category = ModificationCategoryBuilder.GiltiInclusion();
        var result = await _rule.ComputeAsync(ctx, category);

        result.Detail.Should().Contain("GILTI_INCLUSION_V1");
        result.Detail.Should().Contain("2024");
    }
}
