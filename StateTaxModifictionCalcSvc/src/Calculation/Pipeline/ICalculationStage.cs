using Calculation.Engine;

namespace Calculation.Pipeline;

/// <summary>
/// A single stage in the modification calculation pipeline.
/// Stages are executed in order: PreApportionment → Apportionment → PostApportionment.
/// Each stage reads from and writes results back into the shared CalculationContext.
/// </summary>
public interface ICalculationStage
{
    string StageName { get; }

    Task ExecuteAsync(CalculationContext context, CancellationToken ct = default);
}
