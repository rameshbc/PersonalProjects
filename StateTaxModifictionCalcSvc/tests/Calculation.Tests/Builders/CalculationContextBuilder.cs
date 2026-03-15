using Calculation.Engine;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Calculation.Tests.Builders;

/// <summary>
/// Fluent builder for constructing a CalculationContext in tests.
/// Provides sensible defaults so tests only configure what they care about.
///
/// Usage:
///   var ctx = new CalculationContextBuilder()
///       .ForJurisdiction(JurisdictionCode.CA)
///       .ForTaxYear(2024)
///       .WithFederalLine("1120_SCH_C_L10_GILTI", 1_000_000m)
///       .Build();
/// </summary>
public sealed class CalculationContextBuilder
{
    private Guid _jobId = Guid.NewGuid();
    private Guid _clientId = Guid.NewGuid();
    private EntityType _entityType = EntityType.Form1120;
    private JurisdictionCode _jurisdictionCode = JurisdictionCode.US;
    private int _taxYear = DateTime.UtcNow.Year;
    private ApportionmentMethod _apportionmentMethod = ApportionmentMethod.SingleSales;
    private FilingMethod _filingMethod = FilingMethod.Consolidated;

    private readonly Dictionary<string, decimal?> _federalLines = [];
    private readonly List<ModificationCategory> _categories = [];

    // ── Fluent config ──────────────────────────────────────────────────────

    public CalculationContextBuilder WithJobId(Guid id) { _jobId = id; return this; }
    public CalculationContextBuilder WithClientId(Guid id) { _clientId = id; return this; }
    public CalculationContextBuilder WithEntityType(EntityType type) { _entityType = type; return this; }

    public CalculationContextBuilder ForJurisdiction(
        JurisdictionCode code,
        ApportionmentMethod method = ApportionmentMethod.SingleSales)
    {
        _jurisdictionCode = code;
        _apportionmentMethod = method;
        return this;
    }

    public CalculationContextBuilder ForTaxYear(int year) { _taxYear = year; return this; }

    public CalculationContextBuilder WithFederalLine(string lineCode, decimal? value)
    {
        _federalLines[lineCode] = value;
        return this;
    }

    public CalculationContextBuilder WithFederalLines(Dictionary<string, decimal?> lines)
    {
        foreach (var (k, v) in lines) _federalLines[k] = v;
        return this;
    }

    public CalculationContextBuilder WithCategory(ModificationCategory category)
    {
        _categories.Add(category);
        return this;
    }

    public CalculationContextBuilder WithFilingMethod(FilingMethod method)
    {
        _filingMethod = method;
        return this;
    }

    // ── Build ──────────────────────────────────────────────────────────────

    public CalculationContext Build()
    {
        var entity = TaxEntity.Create(
            clientId: _clientId,
            entityName: "Test Entity",
            ein: "99-9999999",
            entityType: _entityType);

        var jurisdiction = Jurisdiction.Create(
            code: _jurisdictionCode,
            level: _jurisdictionCode == JurisdictionCode.US
                ? JurisdictionLevel.Federal : JurisdictionLevel.State,
            name: _jurisdictionCode.ToString(),
            apportionmentMethod: _apportionmentMethod,
            defaultFilingMethod: _filingMethod);

        return new CalculationContext
        {
            JobId = _jobId,
            ClientId = _clientId,
            Entity = entity,
            Jurisdiction = jurisdiction,
            TaxPeriod = TaxPeriod.CalendarYear(_taxYear),
            FilingMethod = _filingMethod,
            FederalReportLines = _federalLines,
            ApplicableCategories = _categories.AsReadOnly()
        };
    }
}
