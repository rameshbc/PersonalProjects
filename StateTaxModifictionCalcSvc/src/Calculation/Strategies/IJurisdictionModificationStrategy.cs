using Calculation.Engine;
using Domain.Entities;
using Domain.ValueObjects;

namespace Calculation.Strategies;

/// <summary>
/// Encapsulates all jurisdiction-specific calculation logic for state modifications.
/// One implementation per jurisdiction (or a shared base for similar states).
///
/// Implementations are resolved at runtime via IJurisdictionStrategyFactory
/// using the jurisdiction's JurisdictionCode.
/// </summary>
public interface IJurisdictionModificationStrategy
{
    /// <summary>
    /// Computes the gross (pre-apportionment) amount for a single modification category.
    /// The strategy may:
    ///   - Read federal report lines from context.FederalReportLines
    ///   - Apply jurisdiction-specific conformity rules (e.g., partial GILTI inclusion)
    ///   - Respect manual overrides already entered by the preparer
    /// </summary>
    Task<ModificationLineResult> ComputePreApportionmentAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default);

    /// <summary>
    /// Computes the apportionment factor for this entity × jurisdiction × period.
    /// Most strategies delegate to a shared ApportionmentCalculator;
    /// special industries (banking, insurance) override with custom formulas.
    /// </summary>
    Task<ApportionmentFactor> ComputeApportionmentFactorAsync(
        CalculationContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Computes the post-apportionment amount for a single modification category.
    /// The apportioned income base is already available in context.
    /// </summary>
    Task<ModificationLineResult> ComputePostApportionmentAsync(
        CalculationContext context,
        ModificationCategory category,
        CancellationToken ct = default);
}
