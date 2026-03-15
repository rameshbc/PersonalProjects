using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// Defines a category of state modification (e.g., "GILTI Inclusion", "163(j) Add-back").
/// Each category references which federal report line feeds the calculation
/// and whether the category is auto-calculated or manual-only.
/// </summary>
public sealed class ModificationCategory
{
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ModificationType DefaultModificationType { get; private set; }
    public ModificationTiming DefaultTiming { get; private set; }

    /// <summary>
    /// Whether the system can auto-calculate this category.
    /// False = manual-only (e.g., state-specific custom adjustments).
    /// </summary>
    public bool IsAutoCalculable { get; private set; }

    /// <summary>
    /// The federal report line (e.g., "Schedule C Line 10 — GILTI") that feeds this mod.
    /// Null for manual categories.
    /// </summary>
    public string? FederalSourceLine { get; private set; }

    /// <summary>IRC section reference for documentation and compliance notes.</summary>
    public string? IRCSection { get; private set; }

    private readonly Dictionary<string, JurisdictionCategoryOverride> _jurisdictionOverrides = [];
    public IReadOnlyDictionary<string, JurisdictionCategoryOverride> JurisdictionOverrides =>
        _jurisdictionOverrides.AsReadOnly();

    private ModificationCategory() { }

    public static ModificationCategory Create(
        string code,
        string description,
        ModificationType defaultType,
        ModificationTiming defaultTiming,
        bool isAutoCalculable,
        string? federalSourceLine = null,
        string? ircSection = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = description,
            DefaultModificationType = defaultType,
            DefaultTiming = defaultTiming,
            IsAutoCalculable = isAutoCalculable,
            FederalSourceLine = federalSourceLine,
            IRCSection = ircSection
        };

    public void AddJurisdictionOverride(string jurisdictionCode, JurisdictionCategoryOverride @override) =>
        _jurisdictionOverrides[jurisdictionCode] = @override;

    public JurisdictionCategoryOverride? GetOverrideFor(string jurisdictionCode) =>
        _jurisdictionOverrides.GetValueOrDefault(jurisdictionCode);
}
