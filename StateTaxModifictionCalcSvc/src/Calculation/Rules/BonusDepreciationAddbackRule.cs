using Calculation.Engine;
using Domain.Entities;
using Domain.Enums;

namespace Calculation.Rules;

/// <summary>
/// Default federal bonus depreciation add-back rule (IRC 168(k)).
/// States that decouple (CA, NY, IL, etc.) override this in their own strategy
/// with state-specific recovery schedules.
///
/// This default rule: add back 100% of federal bonus depreciation claimed.
///
/// Federal lines consumed:
///   1120_M3_L30_BONUS_DEPR   — Federal bonus depreciation (Schedule M-3, Part III, line 30)
/// </summary>
public sealed class BonusDepreciationAddbackRule : IModificationRule
{
    public string RuleId => "BONUS_DEPR_ADDBACK_DEFAULT_V1";

    public bool Applies(ModificationCategory category, CalculationContext context) =>
        category.DefaultModificationType == ModificationType.BonusDepreciationAddback;

    public Task<RuleResult> ComputeAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        var bonusDepr = context.GetFederalLine("1120_M3_L30_BONUS_DEPR");

        var detail = $"IRC 168(k) bonus depr add-back (default 100%): {bonusDepr:C} [Rule:{RuleId}]";

        return Task.FromResult(RuleResult.Of(bonusDepr, detail));
    }
}
