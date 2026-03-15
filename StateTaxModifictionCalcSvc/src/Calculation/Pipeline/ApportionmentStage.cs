using Calculation.Engine;
using Calculation.Strategies;
using Domain.Enums;
using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Calculation.Pipeline;

/// <summary>
/// Stage 2 — computes the apportionment factor and applies it to all
/// pre-apportionment modification results.
///
/// The factor formula (single-sales, double-weighted, three-factor, etc.)
/// is determined by the jurisdiction's ApportionmentMethod setting.
/// The actual factor data (sales numerator/denominator, payroll, property)
/// is provided by the strategy layer.
/// </summary>
public sealed class ApportionmentStage : ICalculationStage
{
    public string StageName => "Apportionment";

    private readonly IJurisdictionStrategyFactory _strategyFactory;
    private readonly ILogger<ApportionmentStage> _logger;

    public ApportionmentStage(
        IJurisdictionStrategyFactory strategyFactory,
        ILogger<ApportionmentStage> logger)
    {
        _strategyFactory = strategyFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(CalculationContext context, CancellationToken ct = default)
    {
        var strategy = _strategyFactory.GetStrategy(context.Jurisdiction.Code, context.TaxPeriod.Year);

        var factor = await strategy.ComputeApportionmentFactorAsync(context, ct);
        context.ComputedApportionmentFactor = factor;

        _logger.LogDebug("Apportionment factor={Factor:P4} Method={Method} Jurisdiction={Code}",
            factor.CombinedFactor, context.Jurisdiction.ApportionmentMethod, context.Jurisdiction.Code);

        if (factor.CombinedFactor < 0 || factor.CombinedFactor > 1)
        {
            context.AddWarning(
                $"Apportionment factor {factor.CombinedFactor:P4} is outside [0,1] — review data.",
                $"Jurisdiction={context.Jurisdiction.Code}");
        }

        // Apply the factor to every pre-apportionment line
        foreach (var (_, result) in context.PreApportionmentResults)
        {
            if (result.IsExcluded) continue;

            result.ApportionedAmount = result.GrossAmount * factor.CombinedFactor;
            result.FinalAmount = result.ApportionedAmount;

            _logger.LogDebug("  [{Code}] Gross={Gross:C} × {Factor:P4} = {Apportioned:C}",
                result.Category.Code, result.GrossAmount,
                factor.CombinedFactor, result.ApportionedAmount);
        }
    }
}
