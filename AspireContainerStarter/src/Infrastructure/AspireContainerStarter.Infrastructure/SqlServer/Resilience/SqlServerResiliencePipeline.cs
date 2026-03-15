using Microsoft.Extensions.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace AspireContainerStarter.Infrastructure.SqlServer.Resilience;

/// <summary>
/// Builds the standard Polly resilience pipeline for Azure SQL connections.
///
/// Strategy (outer → inner):
///   1. Retry  — up to 5 attempts with exponential back-off + jitter.
///               Handles transient SQL errors (timeouts, connection resets).
///   2. Circuit-breaker — opens after 10 consecutive failures for 30 s.
///               Prevents thundering-herd against a degraded SQL instance.
/// </summary>
public static class SqlServerResiliencePipeline
{
    public static void Configure(ResiliencePipelineBuilder builder, SqlServerResilienceOptions options)
    {
        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts    = options.MaxRetryAttempts,
                BackoffType         = DelayBackoffType.Exponential,
                UseJitter           = true,
                Delay               = options.BaseRetryDelay,
                ShouldHandle        = new PredicateBuilder().Handle<Exception>(IsTransient),
                OnRetry             = args =>
                {
                    // Structured log via ResilienceContext — picked up by OpenTelemetry.
                    args.Context.Properties.Set(
                        new ResiliencePropertyKey<string>("sql.retry.reason"),
                        args.Outcome.Exception?.Message ?? "unknown");
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio        = options.CircuitBreakerFailureRatio,
                SamplingDuration    = options.CircuitBreakerSamplingDuration,
                MinimumThroughput   = options.CircuitBreakerMinimumThroughput,
                BreakDuration       = options.CircuitBreakerBreakDuration,
                ShouldHandle        = new PredicateBuilder().Handle<Exception>(IsTransient)
            });
    }

    // Identify SQL errors that are safe to retry (transient by nature).
    private static bool IsTransient(Exception ex) => ex switch
    {
        Microsoft.Data.SqlClient.SqlException sql =>
            sql.Number is
                -2     // Timeout
                or 4060 // Cannot open database
                or 40197 // Service error
                or 40501 // Service busy
                or 40613 // Database unavailable
                or 49918 // Cannot process request
                or 49919 // Cannot process create or update request
                or 49920 // Service is busy
                or 4221,  // Login to read-secondary failed
        TimeoutException => true,
        _ => false
    };
}

public sealed class SqlServerResilienceOptions
{
    public int         MaxRetryAttempts                  { get; set; } = 5;
    public TimeSpan    BaseRetryDelay                    { get; set; } = TimeSpan.FromSeconds(2);
    public double      CircuitBreakerFailureRatio         { get; set; } = 0.5;
    public TimeSpan    CircuitBreakerSamplingDuration     { get; set; } = TimeSpan.FromSeconds(30);
    public int         CircuitBreakerMinimumThroughput    { get; set; } = 10;
    public TimeSpan    CircuitBreakerBreakDuration        { get; set; } = TimeSpan.FromSeconds(30);
}
