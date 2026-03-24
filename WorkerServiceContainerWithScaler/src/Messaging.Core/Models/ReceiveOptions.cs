#nullable enable

namespace Messaging.Core.Models;

public sealed class ReceiveOptions
{
    public ReceiveMode ReceiveMode { get; set; } = ReceiveMode.Push;

    /// <summary>PullBatch only: max messages fetched per cycle (1–N).</summary>
    public int BatchSize { get; set; } = 1;

    /// <summary>PullBatch only: max wait per cycle before looping.</summary>
    public TimeSpan BatchWaitTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public ProcessingMode ProcessingMode { get; set; } = ProcessingMode.Sequential;

    /// <summary>
    /// Parallel mode: max concurrent handler calls.
    /// Push mode: maps to ServiceBusProcessor.MaxConcurrentCalls.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// Pre-fetch buffer (both modes).
    /// Rule of thumb: BatchSize * MaxDegreeOfParallelism * 2.
    /// </summary>
    public int PrefetchCount { get; set; } = 10;
}
