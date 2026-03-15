using Calculation.Strategies;
using Domain.ValueObjects;

namespace Calculation.Tests.Fakes;

/// <summary>
/// In-memory apportionment data provider for tests.
/// Returns configurable factor components without hitting a database.
/// </summary>
public sealed class FakeApportionmentDataProvider : IApportionmentDataProvider
{
    private ApportionmentData _data = new(
        SalesNumerator: 500_000m,
        SalesDenominator: 1_000_000m);

    public FakeApportionmentDataProvider WithFactor(decimal numerator, decimal denominator)
    {
        _data = new ApportionmentData(numerator, denominator);
        return this;
    }

    public FakeApportionmentDataProvider WithThreeFactor(
        decimal salesNum, decimal salesDen,
        decimal payrollNum, decimal payrollDen,
        decimal propNum, decimal propDen)
    {
        _data = new ApportionmentData(
            salesNum, salesDen, payrollNum, payrollDen, propNum, propDen);
        return this;
    }

    public Task<ApportionmentData> GetAsync(
        Guid entityId, Guid jurisdictionId, TaxPeriod period, CancellationToken ct = default) =>
        Task.FromResult(_data);
}
