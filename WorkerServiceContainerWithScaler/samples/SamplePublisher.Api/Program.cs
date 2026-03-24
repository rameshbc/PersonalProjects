using Messaging.Core.Abstractions;
using Messaging.Core.DI;
using Messaging.Core.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServiceBusMessaging(options =>
{
    builder.Configuration.GetSection("Messaging").Bind(options);
    options.Audit.ConnectionString = builder.Configuration.GetConnectionString("AuditDb") ?? string.Empty;
});

var app = builder.Build();

// Liveness probe (used by Dockerfile HEALTHCHECK and Kubernetes probes)
app.MapGet("/healthz", () => Results.Ok("healthy"));

// POST /orders  — publishes to orders-queue
app.MapPost("/orders", async (OrderRequest request, IMessagePublisher publisher, HttpContext http, CancellationToken ct) =>
{
    var rawCorrelationId = http.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    var correlationId = Guid.TryParse(rawCorrelationId, out var parsedGuid)
                        ? parsedGuid.ToString()
                        : Guid.NewGuid().ToString();

    var envelope = new MessageEnvelope
    {
        ClientId      = request.ClientId,   // caller supplies its identity per-message
        CorrelationId = correlationId,
        Subject       = "OrderCreated",
        Body          = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request)
    };

    var result = await publisher.PublishAsync("orders-queue", envelope, ct: ct);

    return result.Status switch
    {
        PublishStatus.Published  => Results.Accepted(value: new { result.MessageId }),
        PublishStatus.Suppressed => Results.StatusCode(429),   // Too Many Requests
        _                        => Results.Problem(result.Exception?.Message ?? "Publish failed")
    };
});

app.Run();

record OrderRequest(string ClientId, string CustomerId, decimal Amount);
