using Calculation.Rules;
using Calculation.Strategies.States;
using Calculation.Tests.Builders;
using Calculation.Tests.Fakes;
using Domain.Enums;
using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Calculation.Tests.Unit.Strategies;

public sealed class CaliforniaStrategyTests
{
    private readonly CaliforniaModificationStrategy _strategy;

    public CaliforniaStrategyTests()
    {
        var rules = new IModificationRule[]
        {
            new GiltiInclusionRule(),
            new BonusDepreciationAddbackRule(),
            new SubpartFInclusionRule(),
            new InterestExpenseAddbackRule()
        };

        _strategy = new CaliforniaModificationStrategy(
            rules,
            new FakeApportionmentDataProvider(),
            NullLogger<CaliforniaModificationStrategy>.Instance);
    }

    [Fact]
    public async Task GILTI_is_excluded_for_California()
    {
        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .WithFederalLine("1120_SCH_C_L10_GILTI", 1_000_000m)
            .Build();

        var category = ModificationCategoryBuilder.GiltiInclusion();
        var result = await _strategy.ComputePreApportionmentAsync(ctx, category);

        result.IsExcluded.Should().BeTrue();
        result.ExclusionReason.Should().Contain("California");
        result.GrossAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Bonus_depreciation_addback_is_net_of_CA_allowed_depreciation()
    {
        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .WithFederalLine("1120_M3_L30_BONUS_DEPR", 500_000m)
            .WithFederalLine("CA_ALLOWED_DEPR", 100_000m)
            .Build();

        var category = ModificationCategoryBuilder.BonusDepreciationAddback();
        var result = await _strategy.ComputePreApportionmentAsync(ctx, category);

        // 500,000 federal bonus - 100,000 CA allowed = 400,000 add-back
        result.GrossAmount.Should().Be(400_000m);
        result.IsExcluded.Should().BeFalse();
    }

    [Fact]
    public async Task Bonus_depreciation_addback_is_zero_when_CA_allows_more_than_federal()
    {
        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .WithFederalLine("1120_M3_L30_BONUS_DEPR", 100_000m)
            .WithFederalLine("CA_ALLOWED_DEPR", 200_000m)
            .Build();

        var category = ModificationCategoryBuilder.BonusDepreciationAddback();
        var result = await _strategy.ComputePreApportionmentAsync(ctx, category);

        result.GrossAmount.Should().Be(0m);
    }

    [Fact]
    public async Task CA_apportionment_uses_single_sales_factor()
    {
        var apportionmentProvider = new FakeApportionmentDataProvider()
            .WithFactor(numerator: 300_000m, denominator: 1_000_000m);

        var strategy = new CaliforniaModificationStrategy(
            new IModificationRule[] { new GiltiInclusionRule() },
            apportionmentProvider,
            NullLogger<CaliforniaModificationStrategy>.Instance);

        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .Build();

        var factor = await strategy.ComputeApportionmentFactorAsync(ctx);

        factor.CombinedFactor.Should().Be(0.30m); // 300k / 1M = 30%
    }

    [Fact]
    public async Task CA_NOL_deduction_is_capped_at_pre_nol_apportioned_income()
    {
        var ctx = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .WithFederalLine("CA_NOL_AVAILABLE_BALANCE", 2_000_000m)
            .Build();

        // Simulate some pre-apportionment results already in context
        ctx.PreApportionmentResults[Guid.NewGuid()] = new Calculation.Engine.ModificationLineResult
        {
            FinalAmount = 500_000m,
            IsExcluded = false
        };

        var nolCategory = ModificationCategoryBuilder.StateNolDeduction();
        var result = await _strategy.ComputePostApportionmentAsync(ctx, nolCategory);

        // NOL capped at 500k (available income), not the full 2M balance
        result.FinalAmount.Should().Be(-500_000m);
    }
}
