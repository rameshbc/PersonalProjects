using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// Jurisdiction-specific override for a modification category —
/// e.g., California partially conforms to GILTI so the timing or type may differ.
/// </summary>
public sealed record JurisdictionCategoryOverride(
    ModificationType? OverrideType,
    ModificationTiming? OverrideTiming,
    bool IsExcluded,
    string? Notes);
