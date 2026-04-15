using Microsoft.Extensions.Logging;

namespace SharepointDocManager.Infrastructure.Graph;

/// <summary>
/// DelegatingHandler that intercepts Graph HTTP responses and handles throttling
/// (HTTP 429 / 503) before the Graph SDK or Polly pipeline ever sees the response.
///
/// Why a DelegatingHandler AND Polly?
/// ────────────────────────────────────
/// The Graph SDK has its own retry logic for some scenarios. This handler sits
/// closer to the wire to enforce Retry-After precisely on every 429 before
/// the SDK retries, preventing "retry storms" where the SDK and Polly both
/// independently retry and multiply the request rate.
///
/// Retry-After header:
///   Graph sets this to the number of seconds to wait. We honour it exactly
///   and add a small random jitter (1–3 s) to spread concurrent clients
///   that were throttled simultaneously.
///
/// Registration: added to the named HttpClient used by GraphServiceClient via
///   builder.Services.AddHttpClient("Graph").AddHttpMessageHandler<GraphThrottlingHandler>()
/// </summary>
public sealed class GraphThrottlingHandler : DelegatingHandler
{
    private const int MaxRetries   = 7;
    private const int DefaultWaitSeconds = 30;

    private readonly ILogger<GraphThrottlingHandler> _logger;

    public GraphThrottlingHandler(ILogger<GraphThrottlingHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var attempt = 0;

        while (true)
        {
            var response = await base.SendAsync(request, ct);

            if (response.StatusCode is not (System.Net.HttpStatusCode.TooManyRequests
                                         or System.Net.HttpStatusCode.ServiceUnavailable))
            {
                return response;  // Fast path — not throttled
            }

            attempt++;
            if (attempt > MaxRetries)
            {
                _logger.LogError(
                    "Graph throttling: exceeded {Max} retries for {Method} {Uri}. Giving up.",
                    MaxRetries, request.Method, request.RequestUri);
                return response;  // Return the 429/503 — Polly handles terminal failure
            }

            var waitSeconds = ParseRetryAfter(response) ?? ComputeBackoff(attempt);
            var jitter      = Random.Shared.Next(1, 4);
            var totalWait   = TimeSpan.FromSeconds(waitSeconds + jitter);

            _logger.LogWarning(
                "Graph throttled ({Status}) — waiting {Wait:g} before retry {Attempt}/{Max}. URI: {Uri}",
                (int)response.StatusCode, totalWait, attempt, MaxRetries, request.RequestUri);

            response.Dispose();
            await Task.Delay(totalWait, ct);

            // Clone the request — HttpRequestMessage cannot be re-sent after disposal
            request = await CloneRequestAsync(request);
        }
    }

    private static int? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return (int)delta.TotalSeconds;
        if (response.Headers.RetryAfter?.Date is { } date)
            return Math.Max(0, (int)(date - DateTimeOffset.UtcNow).TotalSeconds);
        return null;
    }

    private static int ComputeBackoff(int attempt) =>
        Math.Min(120, (int)Math.Pow(2, attempt) * 5);

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
