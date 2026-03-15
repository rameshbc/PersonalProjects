using Calculation.Engine;
using Calculation.Rules;
using Calculation.Strategies;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Calculation.Strategies.States;

/// <summary>
/// New York State (NY) modification strategy.
///
/// Key NY-specific rules:
/// - GILTI: NY conforms to GILTI inclusion (IRC 951A) but allows a 50% subtraction
///   (mirroring the federal 50% deduction under IRC 250) for Article 9-A filers.
///   Combined filers apportion GILTI before applying the 50% deduction.
/// - IRC 163(j): NY conforms; interest expense add-back follows federal treatment.
/// - Bonus Depreciation: NY decouples from IRC 168(k) for property placed in service
///   after 2002. Full add-back in year placed in service; recovery over the MACRS life.
/// - Apportionment: Single-sales factor (market-based sourcing, receipts-factor).
/// - NOL: NY has its own NOL with 20-year carryforward; must use before federal NOL.
/// </summary>
public sealed class NewYorkModificationStrategy : BaseJurisdictionStrategy
{
    private const decimal NyGiltiDeductionRate = 0.50m;

    public NewYorkModificationStrategy(
        IEnumerable<IModificationRule> rules,
        IApportionmentDataProvider apportionmentData,
        ILogger<NewYorkModificationStrategy> logger)
        : base(rules, apportionmentData, logger) { }

    public override async Task<ModificationLineResult> ComputePreApportionmentAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        if (category.DefaultModificationType == ModificationType.GiltiInclusion)
        {
            return await ComputeNyGiltiInclusionAsync(context, category, ct);
        }

        if (category.DefaultModificationType == ModificationType.BonusDepreciationAddback)
        {
            return await ComputeNyBonusDepreciationAddbackAsync(context, category, ct);
        }

        return await base.ComputePreApportionmentAsync(context, category, ct);
    }

    // ── GILTI: include 50% of net GILTI after federal § 250 deduction ──────

    private async Task<ModificationLineResult> ComputeNyGiltiInclusionAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct)
    {
        // Federal GILTI gross income (Schedule C, line 10)
        var giltiGross = context.GetFederalLine("1120_SCH_C_L10_GILTI");

        // High-tax exclusion already applied at federal level
        var highTaxExclusion = context.GetFederalLine("1120_GILTI_HIGH_TAX_EXCL");

        var netGilti = giltiGross - highTaxExclusion;

        // NY allows a 50% subtraction — net inclusion is 50%
        var nyGiltiInclusion = netGilti * (1m - NyGiltiDeductionRate);

        return await Task.FromResult(new ModificationLineResult
        {
            CategoryId = category.Id,
            Category = category,
            GrossAmount = nyGiltiInclusion,
            CalculationDetail =
                $"GILTI gross={giltiGross:C}, High-tax excl={highTaxExclusion:C}, " +
                $"Net GILTI={netGilti:C} × 50% NY inclusion={nyGiltiInclusion:C}"
        });
    }

    // ── Bonus depreciation add-back (full in year 1, recover over life) ────

    private async Task<ModificationLineResult> ComputeNyBonusDepreciationAddbackAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct)
    {
        var federalBonusDepr = context.GetFederalLine("1120_M3_L30_BONUS_DEPR");
        var nyRecoveryAmount = context.GetFederalLine("NY_BONUS_DEPR_RECOVERY");

        // Add back federal bonus, subtract NY recovery (1/5 per year for 5 years)
        var netAddback = federalBonusDepr - nyRecoveryAmount;

        return await Task.FromResult(new ModificationLineResult
        {
            CategoryId = category.Id,
            Category = category,
            GrossAmount = netAddback,
            CalculationDetail =
                $"Federal bonus={federalBonusDepr:C}, NY recovery={nyRecoveryAmount:C}, Net={netAddback:C}"
        });
    }
}
