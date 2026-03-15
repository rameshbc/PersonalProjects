using Calculation.Engine;
using Domain.Entities;
using Domain.Enums;

namespace Calculation.Rules;

/// <summary>
/// Computes the federal GILTI inclusion amount (IRC 951A) that flows into the
/// state modification base. Each jurisdiction strategy then decides how much of
/// this federal amount to include (e.g., 50% for NY/NYC, 0% for CA/IL).
///
/// Federal lines consumed:
///   1120_SCH_C_L10_GILTI       — GILTI gross income (Form 8992, Part II, line 5)
///   1120_GILTI_HIGH_TAX_EXCL   — High-tax exclusion elected under Reg. §1.951A-2(c)(7)
///   1120_SCH_C_L10_SECT78      — IRC 78 gross-up on GILTI
///
/// Tax year: applicable for 2018 and later (TCJA created IRC 951A).
/// </summary>
public sealed class GiltiInclusionRule : IModificationRule
{
    public string RuleId => "GILTI_INCLUSION_V1";

    public bool Applies(ModificationCategory category, CalculationContext context) =>
        category.DefaultModificationType == ModificationType.GiltiInclusion
        && context.TaxPeriod.Year >= 2018
        && context.Entity.IsDomestic;

    public Task<RuleResult> ComputeAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        var giltiGross = context.GetFederalLine("1120_SCH_C_L10_GILTI");
        var highTaxExclusion = context.GetFederalLine("1120_GILTI_HIGH_TAX_EXCL");
        var sect78GrossUp = context.GetFederalLine("1120_SCH_C_L10_SECT78");

        // Net GILTI = gross income minus high-tax exclusion, plus §78 gross-up
        var netGilti = giltiGross - highTaxExclusion + sect78GrossUp;

        var detail =
            $"IRC 951A GILTI Inclusion: Gross={giltiGross:C}, " +
            $"High-tax excl={highTaxExclusion:C}, §78 gross-up={sect78GrossUp:C}, " +
            $"Net={netGilti:C} [Rule:{RuleId} TY:{context.TaxPeriod.Year}]";

        return Task.FromResult(RuleResult.Of(netGilti, detail));
    }
}
