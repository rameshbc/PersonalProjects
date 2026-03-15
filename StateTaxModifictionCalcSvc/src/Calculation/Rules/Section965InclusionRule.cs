using Calculation.Engine;
using Domain.Entities;
using Domain.Enums;

namespace Calculation.Rules;

/// <summary>
/// IRC 965 Transition Tax inclusion (deemed repatriation).
/// Primarily relevant for tax years 2017 and 2018 (TCJA transition).
/// Some states conformed; many imposed full taxation without the federal installment option.
///
/// Federal lines consumed:
///   1120_SCH_C_L6_SECT965         — IRC 965(a) inclusion amount
///   1120_SCH_C_L7_SECT965_DEDUCT  — IRC 965(c) deduction (participation exemption-like)
/// </summary>
public sealed class Section965InclusionRule : IModificationRule
{
    public string RuleId => "SECT965_INCLUSION_V1";

    // 965 is relevant for 2017–2018 (with installments extending longer)
    public bool Applies(ModificationCategory category, CalculationContext context) =>
        category.DefaultModificationType == ModificationType.Section965Inclusion
        && context.TaxPeriod.Year is >= 2017 and <= 2026;

    public Task<RuleResult> ComputeAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        var inclusionAmount = context.GetFederalLine("1120_SCH_C_L6_SECT965");
        var deductionAmount = context.GetFederalLine("1120_SCH_C_L7_SECT965_DEDUCT");

        var netInclusion = inclusionAmount - deductionAmount;

        var detail =
            $"IRC 965: Inclusion={inclusionAmount:C}, §965(c) deduction={deductionAmount:C}, " +
            $"Net={netInclusion:C} [Rule:{RuleId} TY:{context.TaxPeriod.Year}]";

        return Task.FromResult(RuleResult.Of(Math.Max(0, netInclusion), detail));
    }
}
