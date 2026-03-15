using Domain.Enums;

namespace Calculation.Strategies;

/// <summary>
/// Resolves the correct IJurisdictionModificationStrategy for a given
/// jurisdiction + tax year combination.
///
/// Tax-year versioning: rules and state conformity positions change every year
/// (e.g., NY added GILTI 50% inclusion starting 2023; CA has still not conformed).
/// Passing the tax year allows the factory to return the correct versioned strategy.
///
/// Resolution order (first match wins):
///   1. Exact match:   "CA:2024"
///   2. Year fallback: "CA:2023", "CA:2022", ... (nearest prior year registered)
///   3. Code fallback: "CA"      (generic, year-agnostic base)
///   4. Default:       DefaultJurisdictionStrategy
/// </summary>
public interface IJurisdictionStrategyFactory
{
    IJurisdictionModificationStrategy GetStrategy(JurisdictionCode code, int taxYear);
}
