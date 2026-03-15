namespace Calculation.Engine;

/// <summary>
/// Aggregates per-entity traces for a DivCon (divisional consolidation) filing group.
/// Allows troubleshooting the full consolidated picture:
///   - Which entities contributed and how much
///   - Which intercompany eliminations were applied
///   - Net consolidated modification per category
/// </summary>
public sealed class DivConTrace
{
    public Guid JobId { get; init; }
    public Guid FilingGroupId { get; init; }
    public string FilingGroupName { get; init; } = string.Empty;
    public int TaxYear { get; init; }
    public Guid JurisdictionId { get; init; }

    private readonly List<CalculationTrace> _entityTraces = [];
    public IReadOnlyList<CalculationTrace> EntityTraces => _entityTraces.AsReadOnly();

    private readonly List<EliminationEntry> _eliminations = [];
    public IReadOnlyList<EliminationEntry> Eliminations => _eliminations.AsReadOnly();

    public void AddEntityTrace(CalculationTrace trace) => _entityTraces.Add(trace);

    public void AddElimination(string categoryCode, decimal amount, string description) =>
        _eliminations.Add(new EliminationEntry(categoryCode, amount, description, DateTime.UtcNow));

    /// <summary>
    /// Net consolidated amount for a given modification category across all member entities,
    /// after eliminations.
    /// </summary>
    public decimal GetNetConsolidatedAmount(string categoryCode)
    {
        var memberTotal = _entityTraces
            .SelectMany(t => t.Entries)
            .Where(e => e.CategoryCode == categoryCode
                     && e.Level == TraceLevel.Calculation
                     && e.Amounts is not null)
            .Sum(e => e.Amounts!.Output);

        var elimTotal = _eliminations
            .Where(e => e.CategoryCode == categoryCode)
            .Sum(e => e.Amount);

        return memberTotal + elimTotal;
    }

    public string BuildConsolidatedSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== DivCon Trace | Group: {FilingGroupName} | TY: {TaxYear} | Jurisdiction: {JurisdictionId} ===");
        sb.AppendLine($"    Member entities: {_entityTraces.Count}");
        sb.AppendLine();

        foreach (var trace in _entityTraces)
        {
            sb.AppendLine($"  --- Entity: {trace.EntityId} ---");
            foreach (var entry in trace.Entries.Where(e => e.Level == TraceLevel.Calculation))
            {
                sb.AppendLine($"      [{entry.CategoryCode}] {entry.Message}");
            }
        }

        if (_eliminations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  --- Eliminations ---");
            foreach (var elim in _eliminations)
            {
                sb.AppendLine($"      [{elim.CategoryCode}] {elim.Amount:C} — {elim.Description}");
            }
        }

        return sb.ToString();
    }
}

