namespace Calculation.Engine;

public sealed record TraceEntry(
    TraceLevel Level,
    string StageName,
    string? CategoryCode,
    string? RuleId,
    decimal? Factor,
    string Message,
    TraceAmounts? Amounts,
    DateTime Timestamp);
