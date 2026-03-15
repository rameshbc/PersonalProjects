namespace AspireContainerStarter.Infrastructure.SqlServer.Data;

/// <summary>
/// Persisted record of a completed (or failed) calculation job.
/// Both Calc1 and Calc2 jobs write to this single table, discriminated by <see cref="JobType"/>.
/// </summary>
public sealed class CalculationResult
{
    public Guid    Id             { get; set; }
    public Guid    JobId          { get; set; }
    public string  JobType        { get; set; } = string.Empty;   // "Calc1" | "Calc2"
    public string  TaxYear        { get; set; } = string.Empty;
    public string  EntityId       { get; set; } = string.Empty;
    public string? StateCode      { get; set; }                   // null for Calc1
    public string  Status         { get; set; } = string.Empty;
    public string? ResultSummary  { get; set; }
    public DateTimeOffset SubmittedAt  { get; set; }
    public DateTimeOffset CompletedAt  { get; set; }
    public long    DurationMs     { get; set; }
}
