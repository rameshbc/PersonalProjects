namespace Calculation.Strategies;

/// <summary>
/// Apportionment factor numerator/denominator data for all three factors.
/// Returned by IApportionmentDataProvider per entity × jurisdiction × period.
/// </summary>
public sealed record ApportionmentData(
    decimal SalesNumerator,
    decimal SalesDenominator,
    decimal? PayrollNumerator = null,
    decimal? PayrollDenominator = null,
    decimal? PropertyNumerator = null,
    decimal? PropertyDenominator = null);
