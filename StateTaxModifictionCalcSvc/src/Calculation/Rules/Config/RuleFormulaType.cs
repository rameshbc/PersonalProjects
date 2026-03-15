namespace Calculation.Rules.Config;

/// <summary>
/// Describes the arithmetic formula used by a configurable rule.
/// Complex multi-branch logic still requires a code-based IModificationRule;
/// simple linear combinations are handled entirely by configuration.
/// </summary>
public enum RuleFormulaType
{
    /// <summary>result = SUM(input_lines) × rate</summary>
    LinearRate,

    /// <summary>result = line_A - line_B (net of two lines)</summary>
    NetOfTwoLines,

    /// <summary>result = MAX(0, line_A - line_B) × rate</summary>
    NetOfTwoLinesWithFloor,

    /// <summary>result = line_A × percentage_of_line_B</summary>
    PercentageOfLine,

    /// <summary>result = MIN(line_A, line_B)</summary>
    LesserOf,

    /// <summary>
    /// Delegates to a named code-based rule (fallback for complex logic).
    /// The RuleId field identifies which IModificationRule implementation to invoke.
    /// </summary>
    CodeBased
}
