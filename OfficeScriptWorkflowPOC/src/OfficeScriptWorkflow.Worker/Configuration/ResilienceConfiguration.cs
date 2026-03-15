namespace OfficeScriptWorkflow.Worker.Configuration;

public class ResilienceConfiguration
{
    /// <summary>Number of retry attempts before giving up.</summary>
    public int RetryCount { get; set; } = 4;

    /// <summary>Consecutive failures before the circuit opens.</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Seconds the circuit stays open before allowing a probe request.</summary>
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;
}
