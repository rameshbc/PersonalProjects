using Calculation.Engine;
using Calculation.Rules;
using Calculation.Strategies;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Calculation.Strategies.States;

/// <summary>
/// Illinois (IL) modification strategy.
///
/// Key IL-specific rules:
/// - GILTI: Illinois has NOT conformed to GILTI inclusion. No GILTI add-back.
/// - Subpart F: Illinois taxes Subpart F income — add-back required for dividends
///   received deduction amounts that would otherwise exclude Subpart F.
/// - IRC 163(j): Illinois does NOT conform — no add-back for disallowed interest.
/// - Bonus Depreciation: Illinois decouples from bonus depreciation.
///   Add-back 100%; recover on straight-line basis (IL-specific schedule).
/// - Apportionment: Single-sales factor; market-based sourcing for services.
/// - NOL: Illinois NOL is a modified version with an indefinite carryforward
///   but limited to 100% of IL net income.
/// - Illinois also imposes a Personal Property Replacement Tax (PPRT)
///   of 2.5% on corporations — separate from the income tax computation.
/// </summary>
public sealed class IllinoisModificationStrategy : BaseJurisdictionStrategy
{
    public IllinoisModificationStrategy(
        IEnumerable<IModificationRule> rules,
        IApportionmentDataProvider apportionmentData,
        ILogger<IllinoisModificationStrategy> logger)
        : base(rules, apportionmentData, logger) { }

    public override async Task<ModificationLineResult> ComputePreApportionmentAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        // IL does not conform to GILTI — exclude
        if (category.DefaultModificationType is ModificationType.GiltiInclusion
                                             or ModificationType.GiltiDeduction)
        {
            return new ModificationLineResult
            {
                CategoryId = category.Id,
                Category = category,
                IsExcluded = true,
                ExclusionReason = "Illinois does not conform to IRC 951A GILTI."
            };
        }

        // IL does not conform to IRC 163(j) — no add-back
        if (category.DefaultModificationType == ModificationType.InterestExpenseAddback)
        {
            return new ModificationLineResult
            {
                CategoryId = category.Id,
                Category = category,
                IsExcluded = true,
                ExclusionReason = "Illinois does not conform to IRC 163(j) interest limitation."
            };
        }

        if (category.DefaultModificationType == ModificationType.BonusDepreciationAddback)
        {
            return await ComputeIlBonusDepreciationAsync(context, category, ct);
        }

        return await base.ComputePreApportionmentAsync(context, category, ct);
    }

    private async Task<ModificationLineResult> ComputeIlBonusDepreciationAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct)
    {
        var federalBonusDepr = context.GetFederalLine("1120_M3_L30_BONUS_DEPR");
        var ilRecovery = context.GetFederalLine("IL_BONUS_DEPR_RECOVERY");
        var netAddback = federalBonusDepr - ilRecovery;

        return await Task.FromResult(new ModificationLineResult
        {
            CategoryId = category.Id,
            Category = category,
            GrossAmount = netAddback,
            CalculationDetail =
                $"IL bonus depr add-back: Federal={federalBonusDepr:C}, IL recovery={ilRecovery:C}, Net={netAddback:C}"
        });
    }
}
