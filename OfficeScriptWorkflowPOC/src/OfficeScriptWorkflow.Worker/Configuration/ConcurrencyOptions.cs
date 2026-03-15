namespace OfficeScriptWorkflow.Worker.Configuration;

/// <summary>
/// Controls async polling behaviour and HTTP timeouts for Power Automate calls.
/// </summary>
public class ConcurrencyOptions
{
    /// <summary>
    /// How long (seconds) the worker waits for a single Power Automate flow HTTP call
    /// before raising a timeout. Should be ≥ the Office Script's expected runtime.
    /// Power Automate/Logic Apps async 202 polling is handled separately (see below).
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum total wall-clock time the async polling loop will wait for a 202
    /// response to resolve. Power Automate can keep a flow alive for 30 days;
    /// this caps how long the .NET side will poll for a single operation.
    /// </summary>
    public int MaxPollingDurationMinutes { get; set; } = 10;

    /// <summary>
    /// Default interval between polling requests when the flow does not return
    /// a Retry-After header. Power Automate usually returns Retry-After: 10.
    /// </summary>
    public int DefaultPollingIntervalSeconds { get; set; } = 10;
}
