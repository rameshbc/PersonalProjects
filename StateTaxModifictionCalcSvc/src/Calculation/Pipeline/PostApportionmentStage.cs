using Calculation.Engine;
using Calculation.Strategies;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Calculation.Pipeline;

/// <summary>
/// Stage 3 — computes post-apportionment modification amounts.
///
/// Post-apportionment modifications (e.g., state NOL deductions, some DRD)
/// are computed on the already-apportioned base, so no factor is re-applied here.
/// The strategy provides the amount directly.
/// </summary>
public sealed class PostApportionmentStage : ICalculationStage
{
    public string StageName => "PostApportionment";

    private readonly IJurisdictionStrategyFactory _strategyFactory;
    private readonly ILogger<PostApportionmentStage> _logger;

    public PostApportionmentStage(
        IJurisdictionStrategyFactory strategyFactory,
        ILogger<PostApportionmentStage> logger)
    {
        _strategyFactory = strategyFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CalculationContext context, CancellationToken ct = default)
    {
        var strategy = _strategyFactory.GetStrategy(context.Jurisdiction.Code, context.TaxPeriod.Year);

        var postCategories = context.ApplicableCategories
            .Where(c => ResolvedTiming(c, context) == ModificationTiming.PostApportionment)
            .ToList();

        _logger.LogDebug("PostApportionment: {Count} categories for Jurisdiction={Code}",
            postCategories.Count, context.Jurisdiction.Code);

        foreach (var category in postCategories)
        {
            ct.ThrowIfCancellationRequested();

            var jxOverride = category.GetOverrideFor(context.Jurisdiction.Code.ToString());
            if (jxOverride?.IsExcluded == true)
            {
                context.PostApportionmentResults[category.Id] = new ModificationLineResult
                {
                    CategoryId = category.Id,
                    Category = category,
                    IsExcluded = true,
                    ExclusionReason = jxOverride.Notes ?? "Excluded for this jurisdiction"
                };
                continue;
            }

            var result = await strategy.ComputePostApportionmentAsync(context, category, ct);
            context.PostApportionmentResults[category.Id] = result;

            _logger.LogDebug("  [{Code}] {Desc} = {Amount:C}",
                category.Code, category.Description, result.FinalAmount);
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
