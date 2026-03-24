#nullable enable

namespace Messaging.Core.Options;

using Messaging.Core.Models;

public sealed class MessagingOptions
{
    public ServiceBusAuthMode AuthMode { get; set; } = ServiceBusAuthMode.ConnectionString;

    // ConnectionString mode
    public string? ConnectionString { get; set; }

    // ManagedIdentity mode
    /// <summary>e.g. "myns.servicebus.windows.net"</summary>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>null = system-assigned MI; set for user-assigned MI.</summary>
    public string? ManagedIdentityClientId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public bool EnableCompression { get; set; } = false;

    /// <summary>Only compress payloads larger than this size in bytes.</summary>
    public int CompressionThresholdBytes { get; set; } = 1024;

    public RetryPolicyOptions RetryPolicy { get; set; } = new();
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
    public LockRenewalOptions LockRenewal { get; set; } = new();
    public AuditOptions Audit { get; set; } = new();
}

public enum ServiceBusAuthMode { ConnectionString, ManagedIdentity }

public sealed class RetryPolicyOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    public bool UseJitter { get; set; } = true;
}

public sealed class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(60);
}

public sealed class LockRenewalOptions
{
    public bool Enabled { get; set; } = true;
    public int RenewalBufferSeconds { get; set; } = 10;
}
