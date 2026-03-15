using Calculation.Rules;
using Calculation.Strategies;
using Microsoft.Extensions.Logging;

namespace Calculation.Strategies;

/// <summary>
/// Fallback strategy used for any jurisdiction that does not have a
/// dedicated implementation registered. Applies standard federal-conformity
/// rules with no state-specific overrides.
/// </summary>
public sealed class DefaultJurisdictionStrategy : BaseJurisdictionStrategy
{
    public DefaultJurisdictionStrategy(
        IEnumerable<IModificationRule> rules,
        IApportionmentDataProvider apportionmentData,
        ILogger<DefaultJurisdictionStrategy> logger)
        : base(rules, apportionmentData, logger) { }
}
