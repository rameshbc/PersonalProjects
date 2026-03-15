using Calculation.Engine;
using Calculation.Rules;
using Calculation.Strategies;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Calculation.Strategies.Local;

/// <summary>
/// New York City (NYC) General Corporation Tax / Business Corporation Tax strategy.
///
/// NYC largely conforms to New York State modifications, but with key differences:
/// - NYC has its own General Corporation Tax (GCT) / Business Corporation Tax (BCT) base.
/// - NYC uses an "entire net income" base that may differ from NY State.
/// - Apportionment: NYC uses a receipts-only (single-sales) factor measured within NYC.
/// - Minimum tax: NYC imposes a minimum tax based on NY receipts.
/// - GILTI: NYC generally follows NY State — 50% inclusion for BCT.
/// </summary>
public sealed class NewYorkCityModificationStrategy : BaseJurisdictionStrategy
{
    private const decimal NycGiltiDeductionRate = 0.50m;

    public NewYorkCityModificationStrategy(
        IEnumerable<IModificationRule> rules,
        IApportionmentDataProvider apportionmentData,
        ILogger<NewYorkCityModificationStrategy> logger)
        : base(rules, apportionmentData, logger) { }

    public override async Task<ApportionmentFactor> ComputeApportionmentFactorAsync(
        CalculationContext context,
        CancellationToken ct = default)
    {
        // NYC apportionment is receipts (sales) only, measured within NYC city limits
        var data = await _apportionmentData.GetAsync(
            context.Entity.Id, context.Jurisdiction.Id, context.TaxPeriod, ct);

        // NYC receipts factor: NYC-source receipts / total receipts
        return ApportionmentFactor.SingleSales(data.SalesNumerator, data.SalesDenominator);
    }

    public override async Task<ModificationLineResult> ComputePreApportionmentAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default)
    {
        // NYC follows NY state treatment for GILTI — 50% inclusion
        if (category.DefaultModificationType == ModificationType.GiltiInclusion)
        {
            var giltiGross = context.GetFederalLine("1120_SCH_C_L10_GILTI");
            var highTaxExcl = context.GetFederalLine("1120_GILTI_HIGH_TAX_EXCL");
            var nycInclusion = (giltiGross - highTaxExcl) * (1m - NycGiltiDeductionRate);

            return await Task.FromResult(new ModificationLineResult
            {
                CategoryId = category.Id,
                Category = category,
                GrossAmount = nycInclusion,
                CalculationDetail = $"NYC GILTI (50% inclusion): {nycInclusion:C}"
            });
        }

        return await base.ComputePreApportionmentAsync(context, category, ct);
    }
}
