using Calculation.Engine;
using Calculation.Rules;
using Calculation.Strategies;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Calculation.Strategies.States;

/// <summary>
/// California (CA) modification strategy.
///
/// Key CA-specific rules:
/// - GILTI: California has NOT conformed to the IRC 951A GILTI inclusion.
///   CA requires the taxpayer to add back 100% of the GILTI deduction (IRC 250(a)(1)(B))
///   but exclude the GILTI gross-up (IRC 78) from income.
/// - IRC 163(j): California conforms to the federal 163(j) limitation but uses a
///   separate California-specific computation for pass-through entities.
/// - Bonus Depreciation (IRC 168(k)): California does NOT conform — full add-back required.
/// - NOL: California has its own NOL regime with a 20-year carryforward
///   and may have suspended NOL usage in certain years.
/// - Apportionment: Single-sales factor (market-based sourcing since 2013).
/// </summary>
public sealed class CaliforniaModificationStrategy : BaseJurisdictionStrategy
{
    public CaliforniaModificationStrategy(
        IEnumerable<IModificationRule> rules,
        IApportionmentDataProvider apportionmentData,
        ILogger<CaliforniaModificationStrategy> logger)
        : base(rules, apportionmentData, logger) { }

    public override async Task<ModificationLineResult> ComputePreApportionmentAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        // CA does not conform to GILTI — exclude the inclusion entirely.
        if (category.DefaultModificationType == ModificationType.GiltiInclusion)
        {
            return new ModificationLineResult
            {
                CategoryId = category.Id,
                Category = category,
                IsExcluded = true,
                ExclusionReason = "California does not conform to IRC 951A GILTI inclusion."
            };
        }

        // CA does not conform to bonus depreciation — always force full add-back.
        if (category.DefaultModificationType == ModificationType.BonusDepreciationAddback)
        {
            return await ComputeCaBonusDepreciationAddbackAsync(context, category, ct);
        }

        return await base.ComputePreApportionmentAsync(context, category, ct);
    }

    public override async Task<ModificationLineResult> ComputePostApportionmentAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        // CA NOL: post-apportionment deduction capped by CA-specific available NOL balance.
        if (category.DefaultModificationType == ModificationType.NetOperatingLossDeduction)
        {
            return await ComputeCaNolDeductionAsync(context, category, ct);
        }

        return await base.ComputePostApportionmentAsync(context, category, ct);
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private async Task<ModificationLineResult> ComputeCaBonusDepreciationAddbackAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct)
    {
        // Federal bonus depreciation claimed (Schedule M-1 / M-3)
        var federalBonusDepr = context.GetFederalLine("1120_M3_L30_BONUS_DEPR");

        // CA has its own straight-line/MACRS regime — compute the CA-allowed amount
        var caAllowedDepr = context.GetFederalLine("CA_ALLOWED_DEPR");

        var addback = Math.Max(0, federalBonusDepr - caAllowedDepr);

        return await Task.FromResult(new ModificationLineResult
        {
            CategoryId = category.Id,
            Category = category,
            GrossAmount = addback,
            CalculationDetail =
                $"Federal bonus depr={federalBonusDepr:C}, CA allowed={caAllowedDepr:C}, Add-back={addback:C}"
        });
    }

    private async Task<ModificationLineResult> ComputeCaNolDeductionAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct)
    {
        // Post-apportioned income available before NOL
        var apportionedIncome = context.PreApportionmentResults.Values
            .Where(r => !r.IsExcluded)
            .Sum(r => r.FinalAmount);

        // CA NOL available balance (retrieved from prior-year records)
        var caNolAvailable = context.GetFederalLine("CA_NOL_AVAILABLE_BALANCE");

        // CA limits NOL usage to 100% of pre-NOL CA taxable income
        var deduction = Math.Min(caNolAvailable, Math.Max(0, apportionedIncome));

        return await Task.FromResult(new ModificationLineResult
        {
            CategoryId = category.Id,
            Category = category,
            FinalAmount = -deduction, // negative = subtraction
            CalculationDetail =
                $"CA NOL available={caNolAvailable:C}, Pre-NOL income={apportionedIncome:C}, Deduction={deduction:C}"
        });
    }
}
