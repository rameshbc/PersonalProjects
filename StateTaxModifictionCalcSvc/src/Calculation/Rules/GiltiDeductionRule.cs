using Calculation.Engine;
using Domain.Entities;
using Domain.Enums;

namespace Calculation.Rules;

/// <summary>
/// Computes the IRC 250(a)(1)(B) GILTI deduction (50% for 2018–2025; 37.5% for 2026+).
/// Most states decouple — the strategy layer excludes this for non-conforming states.
///
/// Federal lines consumed:
///   1120_SCH_C_L10_GILTI           — GILTI gross income
///   1120_GILTI_HIGH_TAX_EXCL       — High-tax exclusion
///   1120_SCH_C_L36_SECT250_GILTI   — IRC 250 deduction as reported on federal return
/// </summary>
public sealed class GiltiDeductionRule : IModificationRule
{
    public string RuleId => "GILTI_DEDUCTION_V1";

    // Federal deduction rates by tax year (TCJA sunset after 2025)
    private static decimal GetFederalDeductionRate(int taxYear) => taxYear <= 2025 ? 0.50m : 0.375m;

    public bool Applies(ModificationCategory category, CalculationContext context) =>
        category.DefaultModificationType == ModificationType.GiltiDeduction
        && context.TaxPeriod.Year >= 2018;

    public Task<RuleResult> ComputeAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        var federalDeduction = context.GetFederalLine("1120_SCH_C_L36_SECT250_GILTI");
        var rate = GetFederalDeductionRate(context.TaxPeriod.Year);

        var detail =
            $"IRC 250 GILTI deduction: Federal deduction={federalDeduction:C}, " +
            $"Rate={rate:P1} [TY:{context.TaxPeriod.Year} Rule:{RuleId}]";

        // Deduction is a subtraction — return negative
        return Task.FromResult(RuleResult.Of(-Math.Abs(federalDeduction), detail));
    }
}
