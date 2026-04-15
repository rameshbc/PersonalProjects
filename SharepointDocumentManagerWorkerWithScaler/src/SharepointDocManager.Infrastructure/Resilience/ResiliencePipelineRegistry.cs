using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Retry;

namespace SharepointDocManager.Infrastructure.Resilience;

/// <summary>
/// Central registry of Polly v8 resilience pipelines for Graph API calls.
///
/// Two pipeline tiers:
///   "Standard" — default for all clients. Conservative concurrency limits.
///   "Gold"     — premium tier. Higher retry budget, looser circuit breaker.
///
/// Pipelines are keyed by tier name and retrieved via
///   ResiliencePipelineProvider&lt;string&gt; (registered in DI by AddResiliencePipeline).
///
/// Pipeline composition (applied in order):
///   1. Retry           — Respects Retry-After on 429/503; exponential back-off otherwise.
///   2. Circuit breaker — Opens after repeated 5xx to prevent hammering a degraded endpoint.
///
/// Registration in Program.cs:
///   builder.Services.AddResiliencePipeline("Standard", ResiliencePipelineRegistry.ConfigureStandard);
///   builder.Services.AddResiliencePipeline("Gold",     ResiliencePipelineRegistry.ConfigureGold);
/// </summary>
public static class ResiliencePipelineRegistry
{
    public static void ConfigureStandard(ResiliencePipelineBuilder builder, IServiceProvider sp) =>
        Configure(builder, sp, maxRetries: 6, breakDuration: TimeSpan.FromSeconds(60));

    public static void ConfigureGold(ResiliencePipelineBuilder builder, IServiceProvider sp) =>
        Configure(builder, sp, maxRetries: 10, breakDuration: TimeSpan.FromSeconds(30));

    private static void Configure(
        ResiliencePipelineBuilder builder,
        IServiceProvider sp,
        int maxRetries,
        TimeSpan breakDuration)
    {
        var logger = sp.GetRequiredService<ILogger<ResiliencePipelineBuilder>>();

        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetries,
                UseJitter        = true,
                ShouldHandle     = new PredicateBuilder()
                    .Handle<ODataError>(IsTransient)
                    .Handle<HttpRequestException>(IsTransientHttp)
                    .Handle<TaskCanceledException>(),

                DelayGenerator = args =>
                {
                    var wait = ExtractRetryAfter(args.Outcome.Exception)
                               ?? TimeSpan.FromSeconds(Math.Min(120, Math.Pow(2, args.AttemptNumber) * 3));
                    return ValueTask.FromResult<TimeSpan?>(wait);
                },

                OnRetry = args =>
                {
                    logger.LogWarning(
                        "[Polly] Retry {Attempt}/{Max} in {Delay:g}. Reason: {Msg}",
                        args.AttemptNumber + 1, maxRetries,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message ?? "unknown");
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio      = 0.5,
                SamplingDuration  = TimeSpan.FromSeconds(30),
                MinimumThroughput = 10,
                BreakDuration     = breakDuration,
                ShouldHandle      = new PredicateBuilder()
                    .Handle<ODataError>(IsNonThrottlingServerError)
                    .Handle<HttpRequestException>(),

                OnOpened = args =>
                {
                    logger.LogError(
                        "[Polly] Circuit breaker OPEN for {Duration:g}. Last error: {Err}",
                        args.BreakDuration, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("[Polly] Circuit breaker CLOSED — Graph restored.");
                    return ValueTask.CompletedTask;
                }
            });
    }

    private static bool IsTransient(ODataError e) =>
        e.ResponseStatusCode is 429 or 503 or 504 ||
        e.Error?.Code is "activityLimitReached" or "serviceNotAvailable" or "serviceTimeout";

    private static bool IsTransientHttp(HttpRequestException e) =>
        e.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                     or System.Net.HttpStatusCode.ServiceUnavailable
                     or System.Net.HttpStatusCode.GatewayTimeout;

    private static bool IsNonThrottlingServerError(ODataError e) =>
        e.ResponseStatusCode is >= 500 and not (503 or 504);

    private static TimeSpan? ExtractRetryAfter(Exception? ex) =>
        ex is ODataError oda &&
        oda.AdditionalData?.TryGetValue("Retry-After", out var raw) == true &&
        int.TryParse(raw?.ToString(), out var s)
            ? TimeSpan.FromSeconds(s)
            : null;
}
