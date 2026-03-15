using System.Net;
using Microsoft.Extensions.Options;
using OfficeScriptWorkflow.Worker.Configuration;

namespace OfficeScriptWorkflow.Worker.Infrastructure.Http;

/// <summary>
/// DelegatingHandler that implements the HTTP Asynchronous Request-Reply pattern
/// (https://learn.microsoft.com/en-us/azure/architecture/patterns/async-request-reply).
///
/// Power Automate / Logic Apps uses this pattern automatically when a flow run exceeds
/// the synchronous HTTP response threshold (~2 minutes by default):
///
///   1. Client POST → flow returns 202 Accepted
///                    Location: https://prod-XX.../...  (poll URL)
///                    Retry-After: 10
///
///   2. Client GET Location (every Retry-After seconds)
///      → 202 Accepted while still running (Location may update)
///      → 200 OK when complete  (body contains the flow output)
///      → 4xx/5xx on failure
///
/// This handler is transparent to callers — they await PostAsync() and receive the
/// final 200 response, however long the polling takes.
///
/// Configured timeouts:
///   - MaxPollingDuration: absolute wall-clock cap per operation.
///   - DefaultPollingInterval: fallback when Retry-After header is absent.
/// </summary>
public sealed class AsyncPollingHandler : DelegatingHandler
{
    private readonly ConcurrencyOptions _options;
    private readonly ILogger<AsyncPollingHandler> _logger;

    public AsyncPollingHandler(
        IOptions<ConcurrencyOptions> options,
        ILogger<AsyncPollingHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Accepted)
            return response;

        var locationUrl = response.Headers.Location?.ToString();
        if (string.IsNullOrEmpty(locationUrl))
        {
            // 202 without a Location header is not the async polling pattern.
            _logger.LogDebug("Received 202 with no Location header — returning as-is.");
            return response;
        }

        _logger.LogInformation(
            "Flow returned 202 Accepted. Entering async polling loop. MaxDuration: {MaxMin}min",
            _options.MaxPollingDurationMinutes);

        var deadline = DateTimeOffset.UtcNow.AddMinutes(_options.MaxPollingDurationMinutes);
        var defaultInterval = TimeSpan.FromSeconds(_options.DefaultPollingIntervalSeconds);
        int pollAttempt = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Respect Retry-After header; fall back to configured default.
            var waitFor = response.Headers.RetryAfter?.Delta ?? defaultInterval;

            _logger.LogDebug(
                "Poll attempt {Attempt}. Waiting {Wait}s before next poll. Location: {Url}",
                ++pollAttempt, waitFor.TotalSeconds, MaskSasKey(locationUrl));

            await Task.Delay(waitFor, cancellationToken);

            // Location header may update on each 202 response.
            var pollRequest = new HttpRequestMessage(HttpMethod.Get, locationUrl);
            response = await base.SendAsync(pollRequest, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                _logger.LogInformation(
                    "Polling complete after {Attempts} attempts. Final status: {Status}",
                    pollAttempt, (int)response.StatusCode);
                return response;
            }

            // Update location if the response provides a new one.
            var updatedLocation = response.Headers.Location?.ToString();
            if (!string.IsNullOrEmpty(updatedLocation))
                locationUrl = updatedLocation;
        }

        // Deadline exceeded — return the last 202 so the caller can observe the timeout.
        _logger.LogError(
            "Async polling exceeded MaxPollingDuration of {Max}min after {Attempts} attempts.",
            _options.MaxPollingDurationMinutes, pollAttempt);

        return response;
    }

    private static string MaskSasKey(string url)
    {
        var sigIndex = url.IndexOf("sig=", StringComparison.OrdinalIgnoreCase);
        if (sigIndex < 0) return url;
        var end = url.IndexOf('&', sigIndex);
        return end < 0
            ? url[..sigIndex] + "sig=***"
            : url[..sigIndex] + "sig=***" + url[end..];
    }
}
