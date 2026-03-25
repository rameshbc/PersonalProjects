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

    // Body must match OrderCreatedMessage(OrderId, CustomerId, Amount, CreatedAt)
    // that SampleWorker.Queue deserializes. ClientId is an envelope-level field only.
    var messageBody = new
    {
        OrderId   = Guid.NewGuid().ToString("N")[..8].ToUpper(),
        request.CustomerId,
        request.Amount,
        CreatedAt = DateTimeOffset.UtcNow
    };

    var envelope = new MessageEnvelope
    {
        ClientId      = request.ClientId,
        CorrelationId = correlationId,
        Subject       = "OrderCreated",
        Body          = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(messageBody)
    };

    var result = await publisher.PublishAsync("orders-queue", envelope, ct: ct);

    return result.Status switch
    {
        PublishStatus.Published  => Results.Accepted(value: new { result.MessageId }),
        PublishStatus.Suppressed => Results.StatusCode(429),   // Too Many Requests
        _                        => Results.Problem(result.Exception?.Message ?? "Publish failed")
    };
});

// POST /orders/batch  — publishes a list of orders to orders-queue in a single Service Bus batch
app.MapPost("/orders/batch", async (
    IReadOnlyList<OrderRequest> requests,
    IMessagePublisher publisher,
    HttpContext http,
    CancellationToken ct) =>
{
    if (requests.Count == 0)
        return Results.BadRequest("Batch must contain at least one order.");

    var rawCorrelationId = http.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    var batchCorrelationId = Guid.TryParse(rawCorrelationId, out var parsedGuid)
                             ? parsedGuid.ToString()
                             : Guid.NewGuid().ToString();

    var envelopes = requests.Select(r => new MessageEnvelope
    {
        ClientId      = r.ClientId,
        CorrelationId = batchCorrelationId,
        Subject       = "OrderCreated",
        Body          = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            OrderId   = Guid.NewGuid().ToString("N")[..8].ToUpper(),
            r.CustomerId,
            r.Amount,
            CreatedAt = DateTimeOffset.UtcNow
        })
    }).ToList();

    var results = await publisher.PublishBatchAsync("orders-queue", envelopes, ct: ct);

    var published  = results.Count(r => r.Status == PublishStatus.Published);
    var suppressed = results.Count(r => r.Status == PublishStatus.Suppressed);
    var failed     = results.Count(r => r.Status == PublishStatus.PublishFailed);

    return Results.Accepted(value: new { published, suppressed, failed,
        messageIds = results.Select(r => r.MessageId).ToArray() });
});

// GET /audit/trail?destination=orders-queue&since=30
// Returns the full publish→complete trail grouped by MessageId.
// since = lookback window in minutes (default 30). Fetches ALL rows in the window so pairs are never split.
app.MapGet("/audit/trail", async (
    IAuditRepository audit,
    string? destination,
    int since = 30,
    CancellationToken ct = default) =>
{
    var cutoff = DateTime.UtcNow.AddMinutes(-since);
    var entries = await audit.QueryRecentAsync(destination, cutoff, ct: ct);

    var trail = entries
        .GroupBy(e => e.MessageId)
        .Select(g =>
        {
            var pub = g.FirstOrDefault(e => e.OperationType == "Publish");
            var rec = g.FirstOrDefault(e => e.OperationType == "Receive");
            return new
            {
                MessageId     = g.Key,
                CorrelationId = (pub ?? rec)?.CorrelationId,
                Subject       = (pub ?? rec)?.Subject,
                ClientId      = (pub ?? rec)?.ClientId,
                Publish = pub is null ? null : new
                {
                    Status = pub.Status.ToString(),
                    pub.StatusDetail,
                    pub.CreatedAt,
                    pub.UpdatedAt
                },
                Receive = rec is null ? null : new
                {
                    Status = rec.Status.ToString(),
                    rec.StatusDetail,
                    rec.CreatedAt,
                    rec.UpdatedAt
                }
            };
        })
        .OrderByDescending(x => x.Publish?.CreatedAt ?? x.Receive?.CreatedAt);

    return Results.Ok(trail);
});

app.Run();

record OrderRequest(string ClientId, string CustomerId, decimal Amount);
