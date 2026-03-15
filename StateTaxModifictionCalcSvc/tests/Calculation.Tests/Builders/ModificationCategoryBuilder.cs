using Domain.Entities;
using Domain.Enums;

namespace Calculation.Tests.Builders;

/// <summary>
/// Fluent builder for ModificationCategory in tests.
/// </summary>
public sealed class ModificationCategoryBuilder
{
    private string _code = "TEST_MOD";
    private string _description = "Test Modification";
    private ModificationType _type = ModificationType.Addition;
    private ModificationTiming _timing = ModificationTiming.PreApportionment;
    private bool _isAutoCalculable = true;
    private string? _federalSourceLine;
    private string? _ircSection;
    private readonly Dictionary<string, JurisdictionCategoryOverride> _overrides = [];

    public ModificationCategoryBuilder WithCode(string code) { _code = code; return this; }
    public ModificationCategoryBuilder WithDescription(string d) { _description = d; return this; }
    public ModificationCategoryBuilder WithType(ModificationType t) { _type = t; return this; }
    public ModificationCategoryBuilder WithTiming(ModificationTiming t) { _timing = t; return this; }
    public ModificationCategoryBuilder AutoCalculable(bool v = true) { _isAutoCalculable = v; return this; }
    public ModificationCategoryBuilder WithFederalLine(string line) { _federalSourceLine = line; return this; }
    public ModificationCategoryBuilder WithIRC(string section) { _ircSection = section; return this; }

    public ModificationCategoryBuilder ExcludedFor(string jurisdictionCode, string? notes = null)
    {
        _overrides[jurisdictionCode] = new JurisdictionCategoryOverride(
            null, null, IsExcluded: true, Notes: notes);
        return this;
    }

    public ModificationCategoryBuilder OverrideTiming(string jurisdictionCode, ModificationTiming timing)
    {
        _overrides[jurisdictionCode] = new JurisdictionCategoryOverride(
            null, timing, IsExcluded: false, Notes: null);
        return this;
    }

    public ModificationCategory Build()
    {
        var cat = ModificationCategory.Create(
            _code, _description, _type, _timing,
            _isAutoCalculable, _federalSourceLine, _ircSection);
        foreach (var (code, over) in _overrides)
            cat.AddJurisdictionOverride(code, over);
        return cat;
    }

    // ── Common presets ─────────────────────────────────────────────────────

    public static ModificationCategory GiltiInclusion() =>
        new ModificationCategoryBuilder()
            .WithCode("GILTI_INCL")
            .WithDescription("GILTI Inclusion (IRC 951A)")
            .WithType(ModificationType.GiltiInclusion)
            .WithTiming(ModificationTiming.PreApportionment)
            .WithFederalLine("1120_SCH_C_L10_GILTI")
            .WithIRC("951A")
            .ExcludedFor("CA", "California does not conform to GILTI")
            .ExcludedFor("IL", "Illinois does not conform to GILTI")
            .Build();

    public static ModificationCategory BonusDepreciationAddback() =>
        new ModificationCategoryBuilder()
            .WithCode("BONUS_DEPR_ADDBACK")
            .WithDescription("IRC 168(k) Bonus Depreciation Add-back")
            .WithType(ModificationType.BonusDepreciationAddback)
            .WithTiming(ModificationTiming.PreApportionment)
            .WithFederalLine("1120_M3_L30_BONUS_DEPR")
            .WithIRC("168(k)")
            .Build();

    public static ModificationCategory StateNolDeduction() =>
        new ModificationCategoryBuilder()
            .WithCode("STATE_NOL")
            .WithDescription("State Net Operating Loss Deduction")
            .WithType(ModificationType.NetOperatingLossDeduction)
            .WithTiming(ModificationTiming.PostApportionment)
            .AutoCalculable(false) // manual entry per jurisdiction balance
            .Build();
}
