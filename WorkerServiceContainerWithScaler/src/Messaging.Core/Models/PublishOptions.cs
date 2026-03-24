#nullable enable

namespace Messaging.Core.Models;

public sealed class PublishOptions
{
    /// <summary>Override library-level compression setting for this message.</summary>
    public bool? Compress { get; init; }

    /// <summary>When set, suppress publish if pending count >= this value (overrides global setting).</summary>
    public int? MaxPendingBeforeSuppress { get; init; }
}
