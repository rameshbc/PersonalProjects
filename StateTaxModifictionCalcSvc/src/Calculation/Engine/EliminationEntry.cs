namespace Calculation.Engine;

public sealed record EliminationEntry(
    string CategoryCode,
    decimal Amount,
    string Description,
    DateTime Timestamp);
