using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace AspireContainerStarter.Infrastructure.ServiceBus.Resilience;

/// <summary>
/// Polly resilience pipeline for Azure Service Bus publish operations.
///
/// Strategy (outer → inner):
///   1. Retry  — up to 4 attempts with exponential back-off + jitter for
///               transient Service Bus errors (throttling, server busy).
///   2. Circuit-breaker — trips after sustained failures to avoid
///               overwhelming a degraded namespace.
///
/// Note: The ServiceBusClient itself has built-in retry, so this pipeline
/// applies specifically to application-level publish orchestration.
/// </summary>
public static class ServiceBusResiliencePipeline
{
    public static void Configure(ResiliencePipelineBuilder builder, ServiceBusResilienceOptions options)
    {
        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                Delay            = options.BaseRetryDelay,
                ShouldHandle     = new PredicateBuilder().Handle<Exception>(IsTransient)
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio      = options.CircuitBreakerFailureRatio,
                SamplingDuration  = options.CircuitBreakerSamplingDuration,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                BreakDuration     = options.CircuitBreakerBreakDuration,
                ShouldHandle      = new PredicateBuilder().Handle<Exception>(IsTransient)
            });
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        ServiceBusException sbe => sbe.IsTransient,
        TimeoutException        => true,
        _                       => false
    };
}

public sealed class ServiceBusResilienceOptions
{
    public int      MaxRetryAttempts               { get; set; } = 4;
    public TimeSpan BaseRetryDelay                 { get; set; } = TimeSpan.FromSeconds(1);
    public double   CircuitBreakerFailureRatio      { get; set; } = 0.5;
    public TimeSpan CircuitBreakerSamplingDuration  { get; set; } = TimeSpan.FromSeconds(60);
    public int      CircuitBreakerMinimumThroughput { get; set; } = 8;
    public TimeSpan CircuitBreakerBreakDuration     { get; set; } = TimeSpan.FromSeconds(30);
}
