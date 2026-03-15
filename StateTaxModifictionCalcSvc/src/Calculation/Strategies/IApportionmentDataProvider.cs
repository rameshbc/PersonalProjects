using Domain.ValueObjects;

namespace Calculation.Strategies;

public interface IApportionmentDataProvider
{
    Task<ApportionmentData> GetAsync(
        Guid entityId,
        Guid jurisdictionId,
        TaxPeriod period,
        CancellationToken ct = default);
}
