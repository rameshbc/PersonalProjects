namespace Calculation.Rules.Config;

/// <summary>
/// A jurisdiction-specific rate that is valid for a bounded tax year range.
///
/// Used in RuleDefinition.JurisdictionRateOverrides so that a single rule definition
/// can express a jurisdiction's rate history without spawning separate RuleIds per year.
///
/// Example — New York GILTI inclusion rate history:
///   NY: [
///     { "rate": 1.0,  "effectiveFrom": 2018, "effectiveTo": 2022 },  // full inclusion pre-budget
///     { "rate": 0.50, "effectiveFrom": 2023 }                        // 50% after Part CC budget
///   ]
///
/// Resolution: the range whose EffectiveFrom is the highest value that is still ≤ the
/// tax year is selected (most-recent-applicable wins).
/// If no range matches, the rule's top-level Rate is used as the fallback.
/// </summary>
public sealed class JurisdictionRateRange
{
    /// <summary>Rate multiplier for this jurisdiction during this period.</summary>
    public decimal Rate { get; set; }

    /// <summary>First tax year (inclusive) this rate applies.</summary>
    public int EffectiveFrom { get; set; } = 2010;

    /// <summary>Last tax year (inclusive) this rate applies. Null = no sunset.</summary>
    public int? EffectiveTo { get; set; }

    /// <summary>
    /// Optional annotation for audit trail — explains why the rate changed.
    /// E.g. "NY Budget 2022-2023 Part CC — 50% GILTI conformity"
    /// </summary>
    public string? ChangeNote { get; set; }
}
