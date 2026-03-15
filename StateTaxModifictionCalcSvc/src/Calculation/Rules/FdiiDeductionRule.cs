using Calculation.Engine;
using Domain.Entities;
using Domain.Enums;

namespace Calculation.Rules;

/// <summary>
/// Computes the IRC 250(a)(1)(A) FDII deduction (Foreign-Derived Intangible Income).
/// Most states decouple from FDII. The jurisdiction strategy will exclude
/// this rule for states that do not allow the FDII deduction.
///
/// Federal deduction rates:
///   2018–2025: 37.5% of FDII
///   2026+:     21.875% (TCJA sunset — absent congressional action)
///
/// Federal lines consumed:
///   1120_SCH_C_L36_SECT250_FDII   — IRC 250 deduction attributable to FDII
/// </summary>
public sealed class FdiiDeductionRule : IModificationRule
{
    public string RuleId => "FDII_DEDUCTION_V1";

    public bool Applies(ModificationCategory category, CalculationContext context) =>
        category.DefaultModificationType == ModificationType.FdiiDeduction
        && context.TaxPeriod.Year >= 2018
        && context.Entity.IsDomestic;

    public Task<RuleResult> ComputeAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        var federalFdiiDeduction = context.GetFederalLine("1120_SCH_C_L36_SECT250_FDII");

        var detail =
            $"IRC 250 FDII deduction={federalFdiiDeduction:C} [TY:{context.TaxPeriod.Year} Rule:{RuleId}]";

        return Task.FromResult(RuleResult.Of(-Math.Abs(federalFdiiDeduction), detail));
    }
}
