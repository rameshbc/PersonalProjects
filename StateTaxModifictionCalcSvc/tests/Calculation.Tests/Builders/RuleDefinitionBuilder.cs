using Calculation.Rules.Config;

namespace Calculation.Tests.Builders;

/// <summary>
/// Fluent builder for RuleDefinition — used in tests that exercise
/// ConfigurableModificationRule and RuleConfigurationLoader.
///
/// Usage (rate override with year isolation):
///   var def = new RuleDefinitionBuilder()
///       .WithRuleId("GILTI_V1")
///       .ForCategory("GILTI_INCL")
///       .WithFormula(RuleFormulaType.LinearRate)
///       .WithInputLine("LINE_A", sign: 1)
///       .WithDefaultRate(1.0m)
///       .EffectiveFrom(2018)
///       .WithJurisdictionRate("NY", rate: 1.0m, effectiveFrom: 2018, effectiveTo: 2022)
///       .WithJurisdictionRate("NY", rate: 0.50m, effectiveFrom: 2023)
///       .Build();
/// </summary>
public sealed class RuleDefinitionBuilder
{
    private string _ruleId = "TEST_RULE";
    private string _categoryCode = "TEST_CATEGORY";
    private RuleFormulaType _formulaType = RuleFormulaType.LinearRate;
    private decimal _rate = 1.0m;
    private decimal? _maxAmount;
    private decimal? _minInputAmount;
    private int _effectiveFrom = 2010;
    private int? _effectiveTo;
    private readonly List<string> _appliesToJurisdictions = ["ALL"];
    private readonly List<string> _excludedJurisdictions = [];
    private readonly List<RuleLineReference> _inputLines = [];
    private readonly Dictionary<string, List<JurisdictionRateRange>> _rateOverrides = [];
    private string? _ircSection;
    private string? _description;

    // ── Identity ───────────────────────────────────────────────────────────

    public RuleDefinitionBuilder WithRuleId(string id) { _ruleId = id; return this; }
    public RuleDefinitionBuilder ForCategory(string code) { _categoryCode = code; return this; }

    // ── Formula ────────────────────────────────────────────────────────────

    public RuleDefinitionBuilder WithFormula(RuleFormulaType type) { _formulaType = type; return this; }
    public RuleDefinitionBuilder WithDefaultRate(decimal rate) { _rate = rate; return this; }
    public RuleDefinitionBuilder WithMaxAmount(decimal max) { _maxAmount = max; return this; }
    public RuleDefinitionBuilder WithMinInputAmount(decimal min) { _minInputAmount = min; return this; }

    public RuleDefinitionBuilder WithInputLine(string lineCode, int sign = 1, string? description = null)
    {
        _inputLines.Add(new RuleLineReference { LineCode = lineCode, Sign = sign, Description = description });
        return this;
    }

    // ── Year applicability ─────────────────────────────────────────────────

    public RuleDefinitionBuilder EffectiveFrom(int year) { _effectiveFrom = year; return this; }
    public RuleDefinitionBuilder EffectiveTo(int year) { _effectiveTo = year; return this; }

    // ── Jurisdiction applicability ─────────────────────────────────────────

    public RuleDefinitionBuilder ForJurisdictions(params string[] codes)
    {
        _appliesToJurisdictions.Clear();
        _appliesToJurisdictions.AddRange(codes);
        return this;
    }

    public RuleDefinitionBuilder ExcludeJurisdiction(string jurisdictionCode)
    {
        _excludedJurisdictions.Add(jurisdictionCode);
        return this;
    }

    // ── Per-jurisdiction rate ranges ───────────────────────────────────────

    /// <summary>
    /// Adds a rate range for a specific jurisdiction and tax year window.
    /// Call multiple times for the same jurisdiction to express rate history.
    /// </summary>
    public RuleDefinitionBuilder WithJurisdictionRate(
        string jurisdictionCode,
        decimal rate,
        int effectiveFrom,
        int? effectiveTo = null,
        string? changeNote = null)
    {
        if (!_rateOverrides.TryGetValue(jurisdictionCode, out var ranges))
        {
            ranges = [];
            _rateOverrides[jurisdictionCode] = ranges;
        }

        ranges.Add(new JurisdictionRateRange
        {
            Rate = rate,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            ChangeNote = changeNote
        });

        return this;
    }

    // ── Documentation ──────────────────────────────────────────────────────

    public RuleDefinitionBuilder WithIRC(string section) { _ircSection = section; return this; }
    public RuleDefinitionBuilder WithDescription(string desc) { _description = desc; return this; }

    // ── Build ──────────────────────────────────────────────────────────────

    public RuleDefinition Build() => new()
    {
        RuleId = _ruleId,
        CategoryCode = _categoryCode,
        FormulaType = _formulaType,
        Rate = _rate,
        MaximumAmount = _maxAmount,
        MinimumInputAmount = _minInputAmount,
        EffectiveFrom = _effectiveFrom,
        EffectiveTo = _effectiveTo,
        AppliesToJurisdictions = [.. _appliesToJurisdictions],
        ExcludedJurisdictions = [.. _excludedJurisdictions],
        InputLines = [.. _inputLines],
        JurisdictionRateOverrides = _rateOverrides.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToList()),
        IRCSection = _ircSection,
        Description = _description
    };

    // ── Common presets ─────────────────────────────────────────────────────

    /// <summary>Minimal GILTI rule definition wired for test use.</summary>
    public static RuleDefinition GiltiInclusion(decimal defaultRate = 1.0m) =>
        new RuleDefinitionBuilder()
            .WithRuleId("GILTI_INCLUSION_V1")
            .ForCategory("GILTI_INCL")
            .WithFormula(RuleFormulaType.LinearRate)
            .WithInputLine("1120_SCH_C_L10_GILTI", sign: 1)
            .WithInputLine("1120_GILTI_HIGH_TAX_EXCL", sign: -1)
            .WithInputLine("1120_SCH_C_L10_SECT78", sign: 1)
            .WithDefaultRate(defaultRate)
            .EffectiveFrom(2018)
            .ExcludeJurisdiction("CA")
            .ExcludeJurisdiction("IL")
            .WithIRC("951A")
            .Build();

    /// <summary>GILTI rule with NY 50% inclusion from 2023 — mirrors default/gilti.json.</summary>
    public static RuleDefinition GiltiInclusionWithNyHistory() =>
        new RuleDefinitionBuilder()
            .WithRuleId("GILTI_INCLUSION_V1")
            .ForCategory("GILTI_INCL")
            .WithFormula(RuleFormulaType.LinearRate)
            .WithInputLine("1120_SCH_C_L10_GILTI", sign: 1)
            .WithInputLine("1120_GILTI_HIGH_TAX_EXCL", sign: -1)
            .WithInputLine("1120_SCH_C_L10_SECT78", sign: 1)
            .WithDefaultRate(1.0m)
            .EffectiveFrom(2018)
            .ExcludeJurisdiction("CA")
            .ExcludeJurisdiction("IL")
            .WithJurisdictionRate("NY", rate: 1.0m,  effectiveFrom: 2018, effectiveTo: 2022)
            .WithJurisdictionRate("NY", rate: 0.50m, effectiveFrom: 2023, changeNote: "Budget Part CC")
            .WithIRC("951A")
            .Build();
}
