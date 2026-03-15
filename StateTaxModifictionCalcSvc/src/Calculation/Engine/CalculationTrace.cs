using Domain.Enums;

namespace Calculation.Engine;

/// <summary>
/// Detailed step-by-step trace of a calculation run.
/// Captured at every pipeline stage for every modification line.
///
/// Used for:
///   - Troubleshooting individual entity × jurisdiction calculations
///   - DivCon consolidation review (trace shows each member entity's contribution)
///   - Support tickets from clients or preparers
///   - Regression test validation
///
/// Stored separately from the calculation result — not persisted to the main DB
/// unless the job is flagged for detailed tracing.
/// </summary>
public sealed class CalculationTrace
{
    public Guid JobId { get; init; }
    public Guid EntityId { get; init; }
    public Guid JurisdictionId { get; init; }
    public int TaxYear { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; private set; }

    private readonly List<TraceEntry> _entries = [];
    public IReadOnlyList<TraceEntry> Entries => _entries.AsReadOnly();

    // ── Recording helpers ──────────────────────────────────────────────────

    public void RecordStageStart(string stageName) =>
        _entries.Add(new TraceEntry(
            TraceLevel.Info, stageName, null, null, null,
            $"Stage '{stageName}' started", null, DateTime.UtcNow));

    public void RecordLineCalculation(
        string stageName,
        string categoryCode,
        string ruleId,
        decimal inputValue,
        decimal outputValue,
        string detail) =>
        _entries.Add(new TraceEntry(
            TraceLevel.Calculation,
            stageName,
            categoryCode,
            ruleId,
            null,
            detail,
            new TraceAmounts(inputValue, null, outputValue),
            DateTime.UtcNow));

    public void RecordApportionment(
        decimal grossAmount,
        decimal factor,
        decimal apportionedAmount,
        string categoryCode) =>
        _entries.Add(new TraceEntry(
            TraceLevel.Calculation,
            "Apportionment",
            categoryCode,
            null,
            factor,
            $"Gross={grossAmount:C} × Factor={factor:P4} = Apportioned={apportionedAmount:C}",
            new TraceAmounts(grossAmount, factor, apportionedAmount),
            DateTime.UtcNow));

    public void RecordExclusion(string stageName, string categoryCode, string reason) =>
        _entries.Add(new TraceEntry(
            TraceLevel.Excluded, stageName, categoryCode, null, null,
            $"Excluded: {reason}", null, DateTime.UtcNow));

    public void RecordManualOverride(
        string categoryCode,
        decimal systemValue,
        decimal overrideValue,
        string reason) =>
        _entries.Add(new TraceEntry(
            TraceLevel.Override, "Override", categoryCode, null, null,
            $"Manual override: System={systemValue:C} → Override={overrideValue:C}. Reason: {reason}",
            new TraceAmounts(systemValue, null, overrideValue),
            DateTime.UtcNow));

    public void RecordWarning(string stageName, string message) =>
        _entries.Add(new TraceEntry(
            TraceLevel.Warning, stageName, null, null, null, message, null, DateTime.UtcNow));

    public void RecordError(string stageName, string message) =>
        _entries.Add(new TraceEntry(
            TraceLevel.Error, stageName, null, null, null, message, null, DateTime.UtcNow));

    public void Complete() => CompletedAt = DateTime.UtcNow;

    // ── Summary ────────────────────────────────────────────────────────────

    public string BuildSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Calculation Trace | Job: {JobId} | Entity: {EntityId} | Jurisdiction: {JurisdictionId} | TY: {TaxYear} ===");
        sb.AppendLine($"    Started: {StartedAt:u}  Completed: {CompletedAt?.ToString("u") ?? "N/A"}");
        sb.AppendLine();

        foreach (var entry in _entries)
        {
            var prefix = entry.Level switch
            {
                TraceLevel.Calculation => "  CALC",
                TraceLevel.Excluded    => "  EXCL",
                TraceLevel.Override    => "  OVER",
                TraceLevel.Warning     => "  WARN",
                TraceLevel.Error       => "  ERR!",
                _                      => "  INFO"
            };

            var category = entry.CategoryCode is not null ? $"[{entry.CategoryCode}] " : string.Empty;
            var rule = entry.RuleId is not null ? $"({entry.RuleId}) " : string.Empty;
            sb.AppendLine($"{prefix} | {entry.Timestamp:HH:mm:ss.fff} | {category}{rule}{entry.Message}");
        }

        return sb.ToString();
    }
}

