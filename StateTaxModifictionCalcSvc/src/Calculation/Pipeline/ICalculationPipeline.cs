using Calculation.Engine;

namespace Calculation.Pipeline;

public interface ICalculationPipeline
{
    Task ExecuteAsync(CalculationContext context, CancellationToken ct = default);
}
