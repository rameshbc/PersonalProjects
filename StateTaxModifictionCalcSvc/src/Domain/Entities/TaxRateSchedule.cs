namespace Domain.Entities;

/// <summary>
/// Tax rate schedule for a jurisdiction and year.
/// Supports flat, graduated, and min/max rate structures.
/// </summary>
public sealed class TaxRateSchedule
{
    public Guid Id { get; private set; }
    public Guid JurisdictionId { get; private set; }
    public int TaxYear { get; private set; }

    /// <summary>Flat rate (null when graduated brackets apply).</summary>
    public decimal? FlatRate { get; private set; }

    public decimal? MinimumTax { get; private set; }
    public decimal? MaximumTax { get; private set; }

    private readonly List<TaxBracket> _brackets = [];
    public IReadOnlyList<TaxBracket> Brackets => _brackets.AsReadOnly();

    public bool IsGraduated => _brackets.Count > 0;

    private TaxRateSchedule() { }

    public static TaxRateSchedule FlatTax(Guid jurisdictionId, int year, decimal rate,
        decimal? min = null, decimal? max = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            JurisdictionId = jurisdictionId,
            TaxYear = year,
            FlatRate = rate,
            MinimumTax = min,
            MaximumTax = max
        };

    public static TaxRateSchedule Graduated(Guid jurisdictionId, int year,
        IEnumerable<TaxBracket> brackets, decimal? min = null, decimal? max = null)
    {
        var schedule = new TaxRateSchedule
        {
            Id = Guid.NewGuid(),
            JurisdictionId = jurisdictionId,
            TaxYear = year,
            MinimumTax = min,
            MaximumTax = max
        };
        schedule._brackets.AddRange(brackets);
        return schedule;
    }

    /// <summary>Compute gross tax before credits on the given taxable income.</summary>
    public decimal ComputeTax(decimal taxableIncome)
    {
        if (taxableIncome <= 0) return MinimumTax ?? 0m;

        decimal tax;
        if (IsGraduated)
        {
            tax = _brackets
                .OrderBy(b => b.IncomeFloor)
                .Aggregate((remaining: taxableIncome, tax: 0m),
                    (acc, bracket) =>
                    {
                        if (acc.remaining <= 0) return acc;
                        var ceiling = bracket.IncomeCeiling ?? decimal.MaxValue;
                        var taxable = Math.Min(acc.remaining, ceiling - bracket.IncomeFloor);
                        return (acc.remaining - taxable, acc.tax + taxable * bracket.Rate);
                    }).tax;
        }
        else
        {
            tax = taxableIncome * (FlatRate ?? 0m);
        }

        if (MinimumTax.HasValue) tax = Math.Max(tax, MinimumTax.Value);
        if (MaximumTax.HasValue) tax = Math.Min(tax, MaximumTax.Value);
        return tax;
    }
}
