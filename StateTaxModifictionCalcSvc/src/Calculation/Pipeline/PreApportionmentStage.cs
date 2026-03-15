using Calculation.Engine;
using Calculation.Strategies;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Calculation.Pipeline;

/// <summary>
/// Stage 1 — computes all pre-apportionment modification amounts.
///
/// For each applicable category whose timing is PreApportionment:
///   1. Delegates to the jurisdiction-specific strategy to compute the gross amount.
///   2. Stores the result in context.PreApportionmentResults.
///
/// Manual overrides already stored on the modification record are respected —
/// the strategy layer checks for overrides before invoking auto-calculation logic.
/// </summary>
public sealed class PreApportionmentStage : ICalculationStage
{
    public string StageName => "PreApportionment";

    private readonly IJurisdictionStrategyFactory _strategyFactory;
    private readonly ILogger<PreApportionmentStage> _logger;

    public PreApportionmentStage(
        IJurisdictionStrategyFactory strategyFactory,
        ILogger<PreApportionmentStage> logger)
    {
        _strategyFactory = strategyFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CalculationContext context, CancellationToken ct = default)
    {
        var strategy = _strategyFactory.GetStrategy(context.Jurisdiction.Code, context.TaxPeriod.Year);

        var preCategories = context.ApplicableCategories
            .Where(c => ResolvedTiming(c, context) == ModificationTiming.PreApportionment)
            .ToList();

        _logger.LogDebug("PreApportionment: {Count} categories for Jurisdiction={Code}",
            preCategories.Count, context.Jurisdiction.Code);

        foreach (var category in preCategories)
        {
            ct.ThrowIfCancellationRequested();

            // Check jurisdiction-level override — category may be excluded for this state
            var jxOverride = category.GetOverrideFor(context.Jurisdiction.Code.ToString());
            if (jxOverride?.IsExcluded == true)
            {
                context.PreApportionmentResults[category.Id] = new ModificationLineResult
                {
                    CategoryId = category.Id,
                    Category = category,
                    IsExcluded = true,
                    ExclusionReason = jxOverride.Notes ?? "Excluded for this jurisdiction"
                };
                continue;
            }

            var result = await strategy.ComputePreApportionmentAsync(context, category, ct);
            context.PreApportionmentResults[category.Id] = result;

            _logger.LogDebug("  [{Code}] {Desc} = {Amount:C}",
                category.Code, category.Description, result.GrossAmount);
        }
    }

    private static ModificationTiming ResolvedTiming(
        Domain.Entities.ModificationCategory category,
        CalculationContext context)
    {
        var jxOverride = category.GetOverrideFor(context.Jurisdiction.Code.ToString());
        return jxOverride?.OverrideTiming ?? category.DefaultTiming;
    }
}
