using AspireContainerStarter.Api.Hubs;
using AspireContainerStarter.Api.Services;
using AspireContainerStarter.Contracts.Messages;
using AspireContainerStarter.Infrastructure.AppConfig.Extensions;
using AspireContainerStarter.Infrastructure.KeyVault.Extensions;
using AspireContainerStarter.Infrastructure.Redis.Extensions;
using AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;
using AspireContainerStarter.Infrastructure.ServiceBus.Extensions;
using AspireContainerStarter.Infrastructure.ServiceBus.Implementations;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.SignalR;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Aspire service defaults ──────────────────────────────────────────────
// Adds OpenTelemetry, health checks, service discovery, and resilience.
builder.AddServiceDefaults();

// ─── Configuration providers ──────────────────────────────────────────────
// Both are no-ops locally (no connection string → graceful skip).
// In Azure, Aspire injects endpoints via WithReference() in the AppHost.
builder.AddAzureAppConfigurationWithManagedIdentity();
builder.AddAzureKeyVaultWithManagedIdentity();

// ─── OpenAPI ──────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ─── Azure Redis Cache (MI auth) ──────────────────────────────────────────
var redisHost = builder.Configuration["ConnectionStrings:redis-cache"]
    ?? builder.Configuration["Redis:HostName"]
    ?? "localhost";

// In dev Aspire injects "ConnectionStrings:redis-cache" pointing to the local container.
// In Azure it points to the Azure Cache for Redis hostname.
if (!redisHost.Contains("localhost", StringComparison.OrdinalIgnoreCase)
    && !redisHost.Contains("127.0.0.1"))
{
    // Azure Cache for Redis — use MI auth.
    builder.Services.AddAzureRedisCacheWithManagedIdentity(redisHost);
}
else
{
    // Local dev container — plain connection.
    builder.Services.AddStackExchangeRedisCache(opt => opt.Configuration = redisHost);
}

// ─── Azure Service Bus (MI auth) ──────────────────────────────────────────
var sbNamespace = builder.Configuration["ConnectionStrings:service-bus"]
    ?? builder.Configuration["ServiceBus:FullyQualifiedNamespace"]!;

// Publishers — one per destination queue, registered as keyed singletons so
// both can coexist. Inject with [FromKeyedServices("calc1")] in endpoints.
builder.Services.AddAzureServiceBusPublisherWithManagedIdentity(
    fullyQualifiedNamespace: sbNamespace,
    queueOrTopicName: "calc1-jobs",
    serviceKey: "calc1");

builder.Services.AddAzureServiceBusPublisherWithManagedIdentity(
    fullyQualifiedNamespace: sbNamespace,
    queueOrTopicName: "calc2-jobs",
    serviceKey: "calc2");

// Consumer — listens on the job-progress TOPIC SUBSCRIPTION (not a queue).
// Workers publish JobProgressMessage to the topic; this subscription fans it to the API.
builder.Services.AddAzureServiceBusTopicConsumerWithManagedIdentity<
    JobProgressMessage,
    JobProgressNotificationService>(
    fullyQualifiedNamespace: sbNamespace,
    topicName: builder.Configuration["ServiceBus:ProgressTopic"] ?? "job-progress",
    subscriptionName: builder.Configuration["ServiceBus:ProgressSubscription"] ?? "api-progress-subscription");

// Hosted processor — starts the ServiceBusProcessor for the topic subscription above.
builder.Services.AddHostedService<ServiceBusProcessorHostedService<JobProgressMessage>>();

// ─── SignalR ───────────────────────────────────────────────────────────────
var signalRSection = builder.Configuration.GetSection("AzureSignalR");
var signalREndpoint = signalRSection["Endpoint"];

if (!string.IsNullOrWhiteSpace(signalREndpoint))
{
    // Azure SignalR Service — scales across API instances automatically.
    builder.Services.AddSignalR()
        .AddAzureSignalR(opts =>
        {
            opts.ConnectionString = null!;   // No key-based connection string.
            opts.Endpoints = [new ServiceEndpoint(
                new Uri(signalREndpoint),
                new DefaultAzureCredential())];
        });
}
else
{
    // Local dev — run SignalR in-process (no backplane needed for 1 replica).
    builder.Services.AddSignalR();
}

var app = builder.Build();

// ─── Middleware pipeline ───────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                   // /openapi/v1.json  — raw JSON document
    app.MapScalarApiReference();        // /scalar           — browsable UI
}

app.UseHttpsRedirection();
app.MapDefaultEndpoints();   // /health, /alive — from Aspire ServiceDefaults

// ─── Hubs ──────────────────────────────────────────────────────────────────
app.MapHub<JobProgressHub>("/hubs/job-progress");

// ─── Job dispatch endpoints ───────────────────────────────────────────────
app.MapPost("/jobs/calc1", async (
    Calc1JobMessage msg,
    [FromKeyedServices("calc1")] IMessagePublisher publisher,
    CancellationToken ct) =>
{
    await publisher.PublishAsync(msg, msg.JobId.ToString(), ct);
    return Results.Accepted($"/jobs/{msg.JobId}", new { msg.JobId });
})
.WithName("SubmitCalc1Job")
.WithSummary("Submit a Calc1 calculation job");

app.MapPost("/jobs/calc2", async (
    Calc2JobMessage msg,
    [FromKeyedServices("calc2")] IMessagePublisher publisher,
    CancellationToken ct) =>
{
    await publisher.PublishAsync(msg, msg.JobId.ToString(), ct);
    return Results.Accepted($"/jobs/{msg.JobId}", new { msg.JobId });
})
.WithName("SubmitCalc2Job")
.WithSummary("Submit a Calc2 calculation job");

await app.RunAsync();
