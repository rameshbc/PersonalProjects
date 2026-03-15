namespace AspireContainerStarter.Infrastructure.ServiceBus.Options;

/// <summary>
/// Configures adaptive in-process concurrency for
/// <see cref="Implementations.AdaptiveServiceBusProcessorHostedService{TMessage}"/>.
///
/// The monitor loop runs every <see cref="MonitorIntervalSeconds"/> seconds,
/// calculates the queue depth growth rate, and adjusts the effective concurrency
/// ceiling between <see cref="MinConcurrency"/> and <see cref="MaxConcurrency"/>.
/// </summary>
public sealed class AdaptiveConcurrencyOptions
{
    /// <summary>Minimum concurrent message handlers (floor). Default: 2.</summary>
    public int MinConcurrency { get; set; } = 2;

    /// <summary>Maximum concurrent message handlers per instance (ceiling). Default: 20.</summary>
    public int MaxConcurrency { get; set; } = 20;

    /// <summary>How often (seconds) to check queue depth and adjust concurrency. Default: 15.</summary>
    public int MonitorIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Queue depth growth rate (messages/sec) that triggers a scale-up.
    /// If the rate exceeds this threshold, concurrency is increased by 1. Default: 10.
    /// </summary>
    public int GrowthThreshold { get; set; } = 10;

    /// <summary>Queue name used by the monitor loop to poll active message count.</summary>
    internal string QueueName { get; set; } = string.Empty;
}
