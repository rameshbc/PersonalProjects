namespace Calculation.Rules.Config;

/// <summary>
/// A single rule definition — loaded from JSON/YAML configuration.
/// Describes what to compute for a given modification category and tax year range.
///
/// Example (JSON):
/// {
///   "ruleId": "GILTI_INCLUSION_V1",
///   "categoryCode": "GILTI_INCL",
///   "formulaType": "LinearRate",
///   "inputLines": [
///     { "lineCode": "1120_SCH_C_L10_GILTI",     "sign":  1 },
///     { "lineCode": "1120_GILTI_HIGH_TAX_EXCL",  "sign": -1 },
///     { "lineCode": "1120_SCH_C_L10_SECT78",     "sign":  1 }
///   ],
///   "rate": 1.0,
///   "effectiveFrom": 2018,
///   "effectiveTo": null,
///   "appliesTo": ["ALL"],
///   "excludedJurisdictions": ["CA", "IL"]
/// }
/// </summary>
public sealed class RuleDefinition
{
    /// <summary>Unique identifier for audit trails.</summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>Modification category code this rule computes.</summary>
    public string CategoryCode { get; set; } = string.Empty;

    public RuleFormulaType FormulaType { get; set; }

    /// <summary>Input report lines (order matters for NetOfTwoLines).</summary>
    public List<RuleLineReference> InputLines { get; set; } = [];

    /// <summary>Rate multiplier (used by LinearRate, NetOfTwoLinesWithFloor).</summary>
    public decimal Rate { get; set; } = 1.0m;

    /// <summary>Cap amount (null = no cap).</summary>
    public decimal? MaximumAmount { get; set; }

    /// <summary>Floor amount (null = no floor). Applied before rate.</summary>
    public decimal? MinimumInputAmount { get; set; }

    // ── Tax year applicability ─────────────────────────────────────────────

    /// <summary>First tax year this rule applies (inclusive).</summary>
    public int EffectiveFrom { get; set; } = 2010;

    /// <summary>Last tax year this rule applies (null = no sunset).</summary>
    public int? EffectiveTo { get; set; }

    // ── Jurisdiction applicability ─────────────────────────────────────────

    /// <summary>
    /// Jurisdiction codes this rule applies to.
    /// Use ["ALL"] to indicate all jurisdictions (exclusions still apply).
    /// </summary>
    public List<string> AppliesToJurisdictions { get; set; } = ["ALL"];

    /// <summary>Jurisdictions explicitly excluded even if AppliesToJurisdictions = ALL.</summary>
    public List<string> ExcludedJurisdictions { get; set; } = [];

    /// <summary>
    /// For CodeBased formula type: the fully-qualified type name or registered name
    /// of the IModificationRule implementation to delegate to.
    /// </summary>
    public string? CodeBasedRuleTypeName { get; set; }

    // ── Rate overrides per jurisdiction ───────────────────────────────────

    /// <summary>
    /// Jurisdiction-specific rate history, keyed by JurisdictionCode.
    /// Each entry is an ordered list of year-bounded rates; the highest EffectiveFrom
    /// that is still ≤ the calculation tax year is selected.
    ///
    /// If the jurisdiction has no entry, or no range matches the tax year,
    /// the top-level Rate field is used as the fallback.
    ///
    /// Example:
    ///   "NY": [
    ///     { "rate": 1.0,  "effectiveFrom": 2018, "effectiveTo": 2022 },
    ///     { "rate": 0.50, "effectiveFrom": 2023,  "changeNote": "Part CC budget" }
    ///   ]
    /// </summary>
    public Dictionary<string, List<JurisdictionRateRange>> JurisdictionRateOverrides { get; set; } = [];

    // ── Documentation ─────────────────────────────────────────────────────

    public string? IRCSection { get; set; }
    public string? Description { get; set; }
    public string? ChangeNotes { get; set; }
}
