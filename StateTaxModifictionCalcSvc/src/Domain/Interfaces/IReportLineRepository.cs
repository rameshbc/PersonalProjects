using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Interfaces;

/// <summary>
/// Provides access to federal report line values that feed state modifications.
/// These values are pre-populated from the federal return preparation layer.
/// </summary>
public interface IReportLineRepository
{
    /// <summary>
    /// Retrieves a specific report line value for an entity in a given tax period.
    /// Returns null when no value has been entered for the line.
    /// </summary>
    Task<decimal?> GetLineValueAsync(
        Guid entityId,
        string reportLineCode,
        TaxPeriod period,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves multiple report line values in bulk — used by the calculation engine
    /// to avoid N+1 fetches when processing all modifications for an entity.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal?>> GetLineValuesAsync(
        Guid entityId,
        IEnumerable<string> reportLineCodes,
        TaxPeriod period,
        CancellationToken ct = default);
}
