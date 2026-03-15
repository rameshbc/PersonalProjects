using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;

namespace Calculation.Engine;

/// <summary>
/// Mutable context object passed through every stage of the calculation pipeline.
/// Stages read inputs from and write results back into this context.
/// </summary>
public sealed class CalculationContext
{
    // ── Inputs ─────────────────────────────────────────────────────────────

    public Guid JobId { get; init; }
    public Guid ClientId { get; init; }
    public TaxEntity Entity { get; init; } = null!;
    public Jurisdiction Jurisdiction { get; init; } = null!;
    public TaxPeriod TaxPeriod { get; init; } = null!;
    public FilingMethod FilingMethod { get; init; }

    /// <summary>
    /// Federal report line values pre-fetched for this entity/period.
    /// Keys are report line codes (e.g., "1120_SCH_C_L10_GILTI").
    /// </summary>
    public IReadOnlyDictionary<string, decimal?> FederalReportLines { get; init; } =
        new Dictionary<string, decimal?>();

    /// <summary>All modification categories applicable to this jurisdiction.</summary>
    public IReadOnlyList<ModificationCategory> ApplicableCategories { get; init; } =
        Array.Empty<ModificationCategory>();

    // ── Intermediate results written by stages ──────────────────────────────

    /// <summary>Modifications produced by PreApportionmentStage. Key = ModificationCategory.Id.</summary>
    public Dictionary<Guid, ModificationLineResult> PreApportionmentResults { get; } = [];

    /// <summary>Apportionment factor computed for this entity/jurisdiction/period.</summary>
    public ApportionmentFactor? ComputedApportionmentFactor { get; set; }

    /// <summary>Post-apportionment modification lines.</summary>
    public Dictionary<Guid, ModificationLineResult> PostApportionmentResults { get; } = [];

    // ── Diagnostics ─────────────────────────────────────────────────────────

    public List<CalculationDiagnostic> Diagnostics { get; } = [];

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void AddInfo(string message, string? detail = null) =>
        Diagnostics.Add(new(DiagnosticSeverity.Info, message, detail));

    public void AddWarning(string message, string? detail = null) =>
        Diagnostics.Add(new(DiagnosticSeverity.Warning, message, detail));

    public void AddError(string message, string? detail = null) =>
        Diagnostics.Add(new(DiagnosticSeverity.Error, message, detail));

    public decimal GetFederalLine(string lineCode) =>
        FederalReportLines.TryGetValue(lineCode, out var val) ? val ?? 0m : 0m;
}
