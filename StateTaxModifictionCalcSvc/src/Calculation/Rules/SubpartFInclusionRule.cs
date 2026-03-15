using Calculation.Engine;
using Domain.Entities;
using Domain.Enums;

namespace Calculation.Rules;

/// <summary>
/// Computes the Subpart F income inclusion (IRC 951) flowing into the state base.
/// Subpart F predates TCJA and most states conform (unlike GILTI).
///
/// Federal lines consumed:
///   1120_SCH_C_L1_SUBPART_F        — Subpart F income per Schedule C, line 1
///   1120_SCH_C_L9_FOREIGN_DIV      — Previously taxed income distributions excluded
///
/// Applicable: all tax years (Subpart F has been effective since 1962).
/// </summary>
public sealed class SubpartFInclusionRule : IModificationRule
{
    public string RuleId => "SUBPART_F_INCLUSION_V1";

    public bool Applies(ModificationCategory category, CalculationContext context) =>
        category.DefaultModificationType == ModificationType.SubpartFInclusion
        && context.Entity.IsDomestic;

    public Task<RuleResult> ComputeAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        var subpartFGross = context.GetFederalLine("1120_SCH_C_L1_SUBPART_F");
        var ptiExclusion = context.GetFederalLine("1120_SCH_C_L9_FOREIGN_DIV");

        var netSubpartF = subpartFGross - ptiExclusion;

        var detail =
            $"IRC 951 Subpart F: Gross={subpartFGross:C}, PTI excl={ptiExclusion:C}, " +
            $"Net={netSubpartF:C} [Rule:{RuleId}]";

        return Task.FromResult(
            netSubpartF <= 0
                ? RuleResult.Zero($"Net Subpart F ≤ 0 — no inclusion. {detail}")
                : RuleResult.Of(netSubpartF, detail));
    }
}
