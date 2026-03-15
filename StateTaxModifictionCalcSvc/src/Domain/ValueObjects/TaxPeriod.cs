namespace Domain.ValueObjects;

/// <summary>
/// Immutable tax period — typically a fiscal or calendar year.
/// </summary>
public sealed record TaxPeriod(int Year, DateOnly PeriodStart, DateOnly PeriodEnd)
{
    public static TaxPeriod CalendarYear(int year) =>
        new(year, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    public bool Contains(DateOnly date) => date >= PeriodStart && date <= PeriodEnd;

    public override string ToString() => $"{Year} ({PeriodStart:MM/dd/yyyy} – {PeriodEnd:MM/dd/yyyy})";
}
