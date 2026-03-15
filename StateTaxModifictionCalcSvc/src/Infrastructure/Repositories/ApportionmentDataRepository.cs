using Calculation.Strategies;
using Domain.ValueObjects;

namespace Infrastructure.Repositories;

/// <summary>
/// Retrieves apportionment factor data from the database.
/// Sales, payroll, and property numerators/denominators per entity × jurisdiction × period.
/// </summary>
public sealed class ApportionmentDataRepository : IApportionmentDataProvider
{
    // TODO: inject DbContext and fetch from ApportionmentFactors table
    public Task<ApportionmentData> GetAsync(
        Guid entityId,
        Guid jurisdictionId,
        TaxPeriod period,
        CancellationToken ct = default)
    {
        // Placeholder — replace with EF Core query against ApportionmentFactor table
        throw new NotImplementedException(
            "ApportionmentDataRepository not yet implemented. " +
            "Use FakeApportionmentDataProvider in tests.");
    }
}
