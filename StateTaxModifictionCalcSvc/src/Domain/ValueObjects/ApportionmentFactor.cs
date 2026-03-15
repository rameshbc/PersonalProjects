namespace Domain.ValueObjects;

/// <summary>
/// Computed apportionment factor for a single jurisdiction in a given period.
/// All component factors are numerator/denominator pairs yielding a ratio [0,1].
/// </summary>
public sealed record ApportionmentFactor
{
    public decimal SalesNumerator { get; init; }
    public decimal SalesDenominator { get; init; }
    public decimal SalesFactor => SalesDenominator == 0 ? 0 : SalesNumerator / SalesDenominator;

    public decimal? PayrollNumerator { get; init; }
    public decimal? PayrollDenominator { get; init; }
    public decimal? PayrollFactor => PayrollDenominator is > 0
        ? PayrollNumerator!.Value / PayrollDenominator.Value
        : null;

    public decimal? PropertyNumerator { get; init; }
    public decimal? PropertyDenominator { get; init; }
    public decimal? PropertyFactor => PropertyDenominator is > 0
        ? PropertyNumerator!.Value / PropertyDenominator.Value
        : null;

    /// <summary>
    /// Combined factor per the jurisdiction's apportionment formula.
    /// Callers should use the result from ApportionmentStage rather than
    /// computing this directly — jurisdiction weighting differs.
    /// </summary>
    public decimal CombinedFactor { get; init; }

    public static ApportionmentFactor SingleSales(decimal numerator, decimal denominator) =>
        new()
        {
            SalesNumerator = numerator,
            SalesDenominator = denominator,
            CombinedFactor = denominator == 0 ? 0 : numerator / denominator
        };
}
