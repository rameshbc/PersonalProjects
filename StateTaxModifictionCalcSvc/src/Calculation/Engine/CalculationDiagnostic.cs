namespace Calculation.Engine;

public sealed record CalculationDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    string? Detail);
