using Microsoft.Extensions.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using StackExchange.Redis;

namespace AspireContainerStarter.Infrastructure.Redis.Resilience;

/// <summary>
/// Polly resilience pipeline for Azure Cache for Redis operations.
///
/// Strategy:
///   1. Retry  — 3 attempts with exponential back-off + jitter.
///               Handles transient network blips and token refresh delays.
///   2. Circuit-breaker — opens after sustained failures to give the
///               cache time to recover without flooding it with requests.
///
/// The pipeline is keyed "azure-redis" and should be applied at the
/// application cache layer (e.g., IDistributedCache call-sites), not
/// within the StackExchange.Redis connection itself.
/// </summary>
public static class RedisResiliencePipeline
{
    public static void Configure(ResiliencePipelineBuilder builder, RedisResilienceOptions options)
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
        RedisConnectionException  => true,
        RedisTimeoutException     => true,
        RedisServerException rse  => rse.Message.Contains("LOADING", StringComparison.OrdinalIgnoreCase)
                                  || rse.Message.Contains("BUSY",    StringComparison.OrdinalIgnoreCase),
        TimeoutException          => true,
        _                         => false
    };
}

public sealed class RedisResilienceOptions
{
    public int      MaxRetryAttempts               { get; set; } = 3;
    public TimeSpan BaseRetryDelay                 { get; set; } = TimeSpan.FromMilliseconds(500);
    public double   CircuitBreakerFailureRatio      { get; set; } = 0.5;
    public TimeSpan CircuitBreakerSamplingDuration  { get; set; } = TimeSpan.FromSeconds(30);
    public int      CircuitBreakerMinimumThroughput { get; set; } = 5;
    public TimeSpan CircuitBreakerBreakDuration     { get; set; } = TimeSpan.FromSeconds(15);
}
