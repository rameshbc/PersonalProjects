using Calculation.Engine;
using Domain.Entities;

namespace Calculation.Rules;

/// <summary>
/// A single, self-contained calculation rule for one modification category.
///
/// Rules are tax-year-aware: each implementation declares which years it covers
/// via the Applies() method. The strategy layer resolves the correct rule by
/// jurisdiction + tax year.
/// </summary>
public interface IModificationRule
{
    /// <summary>Unique rule identifier for audit trail references.</summary>
    string RuleId { get; }

    /// <summary>
    /// Returns true when this rule can compute the given category for the context.
    /// Implementations check ModificationType, jurisdiction, and tax year.
    /// </summary>
    bool Applies(ModificationCategory category, CalculationContext context);

    Task<RuleResult> ComputeAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default);

    /// <summary>Lower values run first. Default = 100.</summary>
    int Priority => 100;
}
