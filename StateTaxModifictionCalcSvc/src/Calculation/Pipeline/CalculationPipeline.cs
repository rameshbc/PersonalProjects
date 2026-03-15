using Calculation.Engine;
using Microsoft.Extensions.Logging;

namespace Calculation.Pipeline;

/// <summary>
/// Executes registered stages sequentially.
/// Stops on error unless the stage is marked as non-blocking.
/// </summary>
public sealed class CalculationPipeline : ICalculationPipeline
{
    private readonly IEnumerable<ICalculationStage> _stages;
    private readonly ILogger<CalculationPipeline> _logger;

    public CalculationPipeline(
        IEnumerable<ICalculationStage> stages,
        ILogger<CalculationPipeline> logger)
    {
        _stages = stages;
        _logger = logger;
    }

    public async Task ExecuteAsync(CalculationContext context, CancellationToken ct = default)
    {
        foreach (var stage in _stages)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogDebug("Entering stage {Stage} for Entity={EntityId}",
                stage.StageName, context.Entity.Id);

            try
            {
                await stage.ExecuteAsync(context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage {Stage} threw an exception for Entity={EntityId}",
                    stage.StageName, context.Entity.Id);
                context.AddError($"Stage '{stage.StageName}' failed: {ex.Message}", ex.ToString());
                return; // abort remaining stages
            }

            if (context.HasErrors)
            {
                _logger.LogWarning("Stage {Stage} produced errors — aborting pipeline for Entity={EntityId}",
                    stage.StageName, context.Entity.Id);
                return;
            }
        }
    }
}
