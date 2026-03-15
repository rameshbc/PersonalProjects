using Microsoft.Extensions.Logging;

namespace AspireContainerStarter.AppHost.Tests;

/// <summary>
/// Aspire integration tests that boot the entire AppHost in-process
/// and verify that each resource starts correctly.
///
/// NOTE: These tests require Docker to be running (for SQL Server + Redis containers).
/// In CI they run on the ubuntu-latest runner with Docker pre-installed.
/// </summary>
public sealed class AppHostIntegrationTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task AppHost_StartsAllResourcesHealthy()
    {
        // Arrange
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var ct        = cts.Token;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AspireContainerStarter_AppHost>(ct);

        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Aspire.", LogLevel.Warning);
        });

        await using var app = await appHost.BuildAsync(ct).WaitAsync(DefaultTimeout, ct);
        await app.StartAsync(ct).WaitAsync(DefaultTimeout, ct);

        // Act & Assert — wait for each container / resource to become healthy.
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("sql-server", ct)
            .WaitAsync(DefaultTimeout, ct);

        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("redis-cache", ct)
            .WaitAsync(DefaultTimeout, ct);
    }

    [Fact]
    public async Task Api_ReturnsOkOnHealthEndpoint()
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var ct        = cts.Token;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AspireContainerStarter_AppHost>(ct);

        await using var app = await appHost.BuildAsync(ct).WaitAsync(DefaultTimeout, ct);
        await app.StartAsync(ct).WaitAsync(DefaultTimeout, ct);

        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("api", ct)
            .WaitAsync(DefaultTimeout, ct);

        using var client   = app.CreateHttpClient("api");
        using var response = await client.GetAsync("/health", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_AcceptsJobSubmissionOnFedEndpoint()
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var ct        = cts.Token;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AspireContainerStarter_AppHost>(ct);

        await using var app = await appHost.BuildAsync(ct).WaitAsync(DefaultTimeout, ct);
        await app.StartAsync(ct).WaitAsync(DefaultTimeout, ct);

        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("api", ct)
            .WaitAsync(DefaultTimeout, ct);

        using var client = app.CreateHttpClient("api");
        var payload      = System.Text.Json.JsonSerializer.Serialize(new
        {
            jobId       = Guid.NewGuid(),
            taxYear     = "2024",
            entityId    = "E001",
            submittedAt = DateTimeOffset.UtcNow
        });

        using var content  = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/jobs/fed", content, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
