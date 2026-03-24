namespace SampleWorker.Topic.Handlers;

using Messaging.Core.Abstractions;
using Messaging.Core.Models;
using Microsoft.Extensions.Logging;
using SampleWorker.Topic.Messages;

public sealed class OrderShippedHandler : IMessageHandler<OrderShippedMessage>
{
    private readonly ILogger<OrderShippedHandler> _logger;
    public OrderShippedHandler(ILogger<OrderShippedHandler> logger) => _logger = logger;

    public async Task HandleAsync(OrderShippedMessage message, MessageContext context, CancellationToken ct)
    {
        _logger.LogInformation("Order {OrderId} shipped, tracking: {Tracking}", message.OrderId, message.TrackingNumber);
        await Task.Delay(50, ct);
    }
}
