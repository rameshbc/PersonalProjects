namespace Messaging.Core.Resilience;

using Azure.Messaging.ServiceBus;
using Messaging.Core.Options;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

internal static class ServiceBusResiliencePipeline
{
    internal static ResiliencePipeline Build(MessagingOptions opts, ILogger logger)
    {
        var retry = opts.RetryPolicy;
        var cb    = opts.CircuitBreaker;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = retry.MaxRetries,
                Delay            = retry.BaseDelay,
                MaxDelay         = retry.MaxDelay,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = retry.UseJitter,
                ShouldHandle     = new PredicateBuilder()
                    .Handle<ServiceBusException>(ex =>
                        ex.Reason == ServiceBusFailureReason.ServiceBusy ||
                        ex.Reason == ServiceBusFailureReason.ServiceTimeout ||
                        ex.Reason == ServiceBusFailureReason.QuotaExceeded ||
                        ex.IsTransient)
                    .Handle<TimeoutRejectedException>(),
                OnRetry = args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "Service Bus transient error. Retry {Attempt}/{Max} after {Delay}.",
                        args.AttemptNumber + 1, retry.MaxRetries, args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio            = 0.5,
                MinimumThroughput       = cb.FailureThreshold,
                SamplingDuration        = cb.SamplingDuration,
                BreakDuration           = cb.BreakDuration,
                ShouldHandle            = new PredicateBuilder().Handle<Exception>(),
                OnOpened = args =>
                {
                    logger.LogError("Circuit breaker opened for {Duration}s.",
                        cb.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("Circuit breaker closed.");
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();
    }
}
