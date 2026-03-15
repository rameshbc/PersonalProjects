using System.Net;
using Polly;
using Polly.Extensions.Http;

namespace OfficeScriptWorkflow.Worker.Infrastructure.Resilience;

/// <summary>
/// Polly resilience policies for Power Automate HTTP calls.
///
/// Why both retry AND circuit breaker?
/// - Retry handles transient failures (network blips, momentary 503s, throttling).
/// - Circuit breaker stops hammering a flow that is genuinely down,
///   giving it time to recover before the next probe request.
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>
    /// Exponential backoff with jitter. Respects Retry-After headers from Power Automate
    /// 429 (throttled) responses, which Power Automate sends when flow run limits are hit.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(
        int retryCount = 4,
        ILogger? logger = null)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: retryCount,
                sleepDurationProvider: (attempt, outcome, _) =>
                {
                    // Honour Retry-After if present (Power Automate sends this on 429).
                    var retryAfter = outcome.Result?.Headers.RetryAfter?.Delta;
                    if (retryAfter.HasValue) return retryAfter.Value;

                    // Exponential backoff with ±500ms jitter.
                    return TimeSpan.FromSeconds(Math.Pow(2, attempt))
                         + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                },
                onRetryAsync: (outcome, timespan, attempt, _) =>
                {
                    logger?.LogWarning(
                        "Retry {Attempt} in {Delay}s. Reason: {Reason}",
                        attempt,
                        timespan.TotalSeconds.ToString("F1"),
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                    return Task.CompletedTask;
                });
    }

    /// <summary>
    /// Prevents cascading failures. After <paramref name="threshold"/> consecutive
    /// failures the circuit opens for <paramref name="breakDuration"/>,
    /// then allows a single probe through.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
        int threshold = 5,
        TimeSpan? breakDuration = null,
        ILogger? logger = null)
    {
        var duration = breakDuration ?? TimeSpan.FromSeconds(30);

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: threshold,
                durationOfBreak: duration,
                onBreak: (outcome, span) =>
                    logger?.LogError(
                        "Circuit OPEN for {Seconds}s. Last error: {Reason}",
                        span.TotalSeconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()),
                onReset: () =>
                    logger?.LogInformation("Circuit CLOSED — service recovered."),
                onHalfOpen: () =>
                    logger?.LogInformation("Circuit HALF-OPEN — probe request allowed."));
    }
}
