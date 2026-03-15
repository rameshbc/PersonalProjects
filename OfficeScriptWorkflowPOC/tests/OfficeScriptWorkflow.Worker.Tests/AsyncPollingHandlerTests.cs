using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OfficeScriptWorkflow.Worker.Configuration;
using OfficeScriptWorkflow.Worker.Infrastructure.Http;

namespace OfficeScriptWorkflow.Worker.Tests;

/// <summary>
/// Tests for AsyncPollingHandler using a fake inner handler that can be scripted
/// to return a sequence of HTTP responses.
/// </summary>
public class AsyncPollingHandlerTests
{
    private static HttpClient BuildClient(
        IReadOnlyList<HttpResponseMessage> responses,
        int maxPollingMinutes = 1,
        int defaultPollingSeconds = 0)
    {
        var options = Options.Create(new ConcurrencyOptions
        {
            MaxPollingDurationMinutes = maxPollingMinutes,
            DefaultPollingIntervalSeconds = defaultPollingSeconds,
            HttpTimeoutSeconds = 120
        });

        var fakeInner = new SequencedHandler(responses);
        var pollingHandler = new AsyncPollingHandler(options, NullLogger<AsyncPollingHandler>.Instance)
        {
            InnerHandler = fakeInner
        };

        return new HttpClient(pollingHandler) { BaseAddress = new Uri("https://flow.example.com") };
    }

    private static HttpResponseMessage Ok(string body = "{}") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Accepted(string? locationUrl = "https://poll.example.com/status", int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Accepted);
        if (locationUrl is not null)
            response.Headers.Location = new Uri(locationUrl);
        if (retryAfterSeconds.HasValue)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds(retryAfterSeconds.Value));
        return response;
    }

    // ── Pass-through for non-202 ───────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_200Response_ReturnedDirectly()
    {
        var client = BuildClient([Ok("{\"result\":\"done\"}")]);

        var response = await client.PostAsync("/trigger", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_404Response_ReturnedDirectly()
    {
        var client = BuildClient([new HttpResponseMessage(HttpStatusCode.NotFound)]);

        var response = await client.PostAsync("/trigger", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── 202 without Location — returned as-is ────────────────────────────────

    [Fact]
    public async Task SendAsync_202WithNoLocation_ReturnedAsIs()
    {
        var accepted = new HttpResponseMessage(HttpStatusCode.Accepted); // no Location
        var client = BuildClient([accepted]);

        var response = await client.PostAsync("/trigger", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    // ── Async polling: 202 → poll → 200 ──────────────────────────────────────

    [Fact]
    public async Task SendAsync_202ThenOk_ReturnsOk()
    {
        // POST → 202 (Location set), GET poll → 200
        var client = BuildClient([Accepted(retryAfterSeconds: 0), Ok()]);

        var response = await client.PostAsync("/trigger", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_202ThenMultiple202ThenOk_ReturnsOk()
    {
        // POST → 202, GET → 202 (still running), GET → 200
        var client = BuildClient([
            Accepted(retryAfterSeconds: 0),
            Accepted(retryAfterSeconds: 0),
            Ok("final result")
        ]);

        var response = await client.PostAsync("/trigger", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("final result", body);
    }

    // ── Updated Location header ───────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_PollResponseUpdatesLocation_FollowsNewLocation()
    {
        // First 202 → Location A; second 202 → Location B (updated); GET B → 200
        var firstAccepted = Accepted("https://poll.example.com/A", retryAfterSeconds: 0);
        var secondAccepted = Accepted("https://poll.example.com/B", retryAfterSeconds: 0);
        var client = BuildClient([firstAccepted, secondAccepted, Ok()]);

        var response = await client.PostAsync("/trigger", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Deadline exceeded ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_DeadlineExceeded_ReturnsLast202()
    {
        // All responses are 202; deadline is very short (1 second via maxPollingMinutes trick)
        // Use a very short timeout with immediate retryAfter to force expiry
        var options = Options.Create(new ConcurrencyOptions
        {
            MaxPollingDurationMinutes = 0,   // deadline is in the past immediately
            DefaultPollingIntervalSeconds = 0,
            HttpTimeoutSeconds = 120
        });

        var fakeInner = new SequencedHandler([Accepted(retryAfterSeconds: 0), Accepted(retryAfterSeconds: 0)]);
        var pollingHandler = new AsyncPollingHandler(options, NullLogger<AsyncPollingHandler>.Instance)
        {
            InnerHandler = fakeInner
        };
        var client = new HttpClient(pollingHandler) { BaseAddress = new Uri("https://flow.example.com") };

        var response = await client.PostAsync("/trigger", new StringContent("{}"));

        // When deadline is exceeded, last 202 is returned
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_CancelledDuringPoll_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();

        // Provide many 202s; cancel after the first poll is scheduled
        var responses = Enumerable.Range(0, 10)
            .Select(_ => Accepted(retryAfterSeconds: 0))
            .ToList<HttpResponseMessage>();

        var counter = new CountingHandler(responses, onSecondCall: cts.Cancel);
        var options = Options.Create(new ConcurrencyOptions
        {
            MaxPollingDurationMinutes = 5,
            DefaultPollingIntervalSeconds = 0,
            HttpTimeoutSeconds = 120
        });
        var pollingHandler = new AsyncPollingHandler(options, NullLogger<AsyncPollingHandler>.Instance)
        {
            InnerHandler = counter
        };
        var httpClient = new HttpClient(pollingHandler) { BaseAddress = new Uri("https://flow.example.com") };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            httpClient.PostAsync("/trigger", new StringContent("{}"), cts.Token));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns responses from a fixed sequence; repeats last on exhaustion.</summary>
    private sealed class SequencedHandler(IReadOnlyList<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var response = responses[Math.Min(_index++, responses.Count - 1)];
            return Task.FromResult(response);
        }
    }

    /// <summary>Invokes a callback after the second call (simulates cancel mid-poll).</summary>
    private sealed class CountingHandler(IReadOnlyList<HttpResponseMessage> responses, Action onSecondCall) : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (++_calls == 2) onSecondCall();
            return Task.FromResult(responses[Math.Min(_calls - 1, responses.Count - 1)]);
        }
    }
}
