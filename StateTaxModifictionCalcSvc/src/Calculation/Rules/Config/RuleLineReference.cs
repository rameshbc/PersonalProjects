namespace Calculation.Rules.Config;

/// <summary>A federal or state report line referenced in a rule formula.</summary>
public sealed class RuleLineReference
{
    /// <summary>Report line code (e.g., "1120_SCH_C_L10_GILTI").</summary>
    public string LineCode { get; set; } = string.Empty;

    /// <summary>Sign to apply when adding this line to a sum (+1 or -1).</summary>
    public int Sign { get; set; } = 1;

    /// <summary>Optional description for documentation.</summary>
    public string? Description { get; set; }
}
