using Domain.Enums;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Represents a taxing jurisdiction — federal, state, or local.
/// Holds the jurisdiction-level configuration used by the calculation engine.
/// </summary>
public sealed class Jurisdiction
{
    public Guid Id { get; private set; }
    public JurisdictionCode Code { get; private set; }
    public JurisdictionLevel Level { get; private set; }
    public string Name { get; private set; } = string.Empty;

    // Tax type flags
    public bool HasIncomeTax { get; private set; }
    public bool HasFranchiseTax { get; private set; }
    public bool HasGrossReceiptsTax { get; private set; }

    // Apportionment
    public ApportionmentMethod ApportionmentMethod { get; private set; }
    public FilingMethod DefaultFilingMethod { get; private set; }

    // Federal conformity
    public bool ConformsToFederalConsolidation { get; private set; }

    /// <summary>
    /// Indicates the state requires combined reporting (unitary business).
    /// </summary>
    public bool RequiresCombinedReporting { get; private set; }

    /// <summary>
    /// Tax rates keyed by tax year. See TaxRateSchedule for graduated/min/max logic.
    /// </summary>
    private readonly List<TaxRateSchedule> _rateSchedules = [];
    public IReadOnlyList<TaxRateSchedule> RateSchedules => _rateSchedules.AsReadOnly();

    private Jurisdiction() { }

    public static Jurisdiction Create(
        JurisdictionCode code,
        JurisdictionLevel level,
        string name,
        ApportionmentMethod apportionmentMethod,
        FilingMethod defaultFilingMethod)
    {
        return new Jurisdiction
        {
            Id = Guid.NewGuid(),
            Code = code,
            Level = level,
            Name = name,
            ApportionmentMethod = apportionmentMethod,
            DefaultFilingMethod = defaultFilingMethod,
            HasIncomeTax = true
        };
    }

    public TaxRateSchedule? GetRateSchedule(int taxYear) =>
        _rateSchedules.SingleOrDefault(s => s.TaxYear == taxYear);

    public void AddRateSchedule(TaxRateSchedule schedule) =>
        _rateSchedules.Add(schedule);
}
