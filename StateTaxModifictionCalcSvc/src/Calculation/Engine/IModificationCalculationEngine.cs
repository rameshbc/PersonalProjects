using Domain.ValueObjects;

namespace Calculation.Engine;

/// <summary>
/// Orchestrates the full state modification calculation for a single
/// entity × jurisdiction × tax period combination.
/// </summary>
public interface IModificationCalculationEngine
{
    /// <summary>
    /// Runs the full pipeline (pre-apportionment → apportionment → post-apportionment)
    /// and returns a completed context with all results and diagnostics.
    /// </summary>
    Task<CalculationContext> CalculateAsync(
        CalculationRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Input to a single calculation run — everything the engine needs to build its context.
/// </summary>
public sealed record CalculationRequest(
    Guid JobId,
    Guid ClientId,
    Guid EntityId,
    Guid JurisdictionId,
    TaxPeriod TaxPeriod);
