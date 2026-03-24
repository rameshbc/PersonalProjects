#nullable enable

namespace Messaging.Core.Options;

public sealed class AuditOptions
{
    public bool Enabled { get; set; } = true;
    public string? ConnectionString { get; set; }
    public bool LogMessageBody { get; set; } = true;
    public int MaxBodyBytesStored { get; set; } = 65_536;
    public int RetentionDays { get; set; } = 90;
    public PendingCheckOptions PendingCheck { get; set; } = new();
}

public sealed class PendingCheckOptions
{
    public bool Enabled { get; set; } = false;
    public int MaxPendingBeforeSuppress { get; set; } = 10;
    public int LookbackWindowMinutes { get; set; } = 60;
}
