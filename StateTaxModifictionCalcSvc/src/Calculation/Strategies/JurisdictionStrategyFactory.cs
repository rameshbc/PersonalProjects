using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Calculation.Strategies;

/// <summary>
/// Resolves jurisdiction strategies from the DI container with tax-year versioning.
///
/// Registration convention in DI:
///   // Year-specific (preferred when conformity changed mid-stream)
///   services.AddKeyedScoped&lt;IJurisdictionModificationStrategy,
///       CaliforniaModificationStrategy&gt;("CA:2024");
///
///   // Year-agnostic fallback for a jurisdiction
///   services.AddKeyedScoped&lt;IJurisdictionModificationStrategy,
///       CaliforniaModificationStrategy&gt;("CA");
///
/// The factory walks backwards from taxYear to the earliest registered year
/// before falling back to the year-agnostic key, then to the DefaultJurisdictionStrategy.
/// </summary>
public sealed class JurisdictionStrategyFactory : IJurisdictionStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;

    // Earliest supported tax year — prevents infinite backward search.
    private const int EarliestSupportedYear = 2010;

    public JurisdictionStrategyFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IJurisdictionModificationStrategy GetStrategy(JurisdictionCode code, int taxYear)
    {
        var codeStr = code.ToString();

        // 1. Exact year match
        var strategy = _serviceProvider
            .GetKeyedService<IJurisdictionModificationStrategy>($"{codeStr}:{taxYear}");
        if (strategy is not null) return strategy;

        // 2. Nearest prior registered year (walk backwards)
        for (int year = taxYear - 1; year >= EarliestSupportedYear; year--)
        {
            strategy = _serviceProvider
                .GetKeyedService<IJurisdictionModificationStrategy>($"{codeStr}:{year}");
            if (strategy is not null) return strategy;
        }

        // 3. Year-agnostic jurisdiction key
        strategy = _serviceProvider
            .GetKeyedService<IJurisdictionModificationStrategy>(codeStr);
        if (strategy is not null) return strategy;

        // 4. Default fallback
        return _serviceProvider.GetRequiredService<DefaultJurisdictionStrategy>();
    }
}
