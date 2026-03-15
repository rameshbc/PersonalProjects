using Calculation.Engine;
using Domain.Entities;
using Domain.Enums;

namespace Calculation.Rules;

/// <summary>
/// Computes the IRC 163(j) business interest expense limitation add-back.
/// Many states conform; some (IL, TX) do not — excluded at the strategy layer.
///
/// 163(j) was significantly modified by TCJA (2018) and CARES Act (2020).
///
/// Federal lines consumed:
///   1120_F8990_L30_DISALLOWED_INT  — Disallowed business interest expense (Form 8990, line 30)
///   1120_F8990_L6_PRIOR_CARRYOVER  — Prior-year carryforward of disallowed interest
///
/// The add-back is the current-year disallowed amount (not prior carryover).
/// </summary>
public sealed class InterestExpenseAddbackRule : IModificationRule
{
    public string RuleId => "INTEREST_163J_ADDBACK_V1";

    public bool Applies(ModificationCategory category, CalculationContext context) =>
        category.DefaultModificationType == ModificationType.InterestExpenseAddback
        && context.TaxPeriod.Year >= 2018;

    public Task<RuleResult> ComputeAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        var disallowedInterest = context.GetFederalLine("1120_F8990_L30_DISALLOWED_INT");
        var priorCarryover = context.GetFederalLine("1120_F8990_L6_PRIOR_CARRYOVER");

        // Add back only current-year disallowed (not carryovers from prior years)
        var currentYearDisallowed = Math.Max(0, disallowedInterest - priorCarryover);

        var detail =
            $"IRC 163(j) add-back: Total disallowed={disallowedInterest:C}, " +
            $"Prior carryover={priorCarryover:C}, Current year={currentYearDisallowed:C} " +
            $"[Rule:{RuleId} TY:{context.TaxPeriod.Year}]";

        return Task.FromResult(RuleResult.Of(currentYearDisallowed, detail));
    }
}
