namespace Calculation.Rules;

public sealed record RuleResult(
    decimal Amount,
    string Detail,
    bool HasWarning = false,
    string? WarningMessage = null)
{
    public static RuleResult Zero(string detail) => new(0m, detail);
    public static RuleResult Of(decimal amount, string detail) => new(amount, detail);
}
