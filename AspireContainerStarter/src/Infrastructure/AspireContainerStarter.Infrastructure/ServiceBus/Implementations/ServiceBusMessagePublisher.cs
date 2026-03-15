using System.Text.Json;
using AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Polly;

namespace AspireContainerStarter.Infrastructure.ServiceBus.Implementations;

/// <summary>
/// Publishes messages to a single Azure Service Bus queue or topic,
/// serialising payloads as UTF-8 JSON and applying the registered
/// resilience pipeline for transient fault handling.
/// </summary>
internal sealed class ServiceBusMessagePublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<ServiceBusMessagePublisher> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ServiceBusMessagePublisher(
        ServiceBusSender sender,
        ResiliencePipeline pipeline,
        ILogger<ServiceBusMessagePublisher> logger)
    {
        _sender   = sender;
        _pipeline = pipeline;
        _logger   = logger;
    }

    public async Task PublishAsync<T>(T message, string? correlationId = null, CancellationToken ct = default)
        where T : class
    {
        var sbMessage = CreateMessage(message, correlationId);

        await _pipeline.ExecuteAsync(async token =>
        {
            await _sender.SendMessageAsync(sbMessage, token);
            _logger.LogDebug(
                "Published message {MessageId} of type {Type} to {Queue}",
                sbMessage.MessageId, typeof(T).Name, _sender.EntityPath);
        }, ct);
    }

    public async Task PublishBatchAsync<T>(IEnumerable<T> messages, CancellationToken ct = default)
        where T : class
    {
        var sbMessages = messages.Select(m => CreateMessage(m, null)).ToList();

        await _pipeline.ExecuteAsync(async token =>
        {
            using var batch = await _sender.CreateMessageBatchAsync(token);
            foreach (var msg in sbMessages)
            {
                if (!batch.TryAddMessage(msg))
                    throw new InvalidOperationException(
                        $"Message of type {typeof(T).Name} is too large for a single batch.");
            }
            await _sender.SendMessagesAsync(batch, token);
            _logger.LogDebug("Published batch of {Count} {Type} messages", sbMessages.Count, typeof(T).Name);
        }, ct);
    }

    private static ServiceBusMessage CreateMessage<T>(T payload, string? correlationId)
    {
        var body    = BinaryData.FromObjectAsJson(payload, _jsonOptions);
        var message = new ServiceBusMessage(body)
        {
            ContentType   = "application/json",
            Subject       = typeof(T).Name,
            MessageId     = Guid.NewGuid().ToString("N"),
            CorrelationId = correlationId
        };
        return message;
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
