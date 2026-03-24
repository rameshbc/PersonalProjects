namespace SampleWorker.Queue.Handlers;

using Messaging.Core.Abstractions;
using Messaging.Core.Models;
using Microsoft.Extensions.Logging;
using SampleWorker.Queue.Messages;

public sealed class OrderCreatedHandler : IMessageHandler<OrderCreatedMessage>
{
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(ILogger<OrderCreatedHandler> logger) => _logger = logger;

    public async Task HandleAsync(OrderCreatedMessage message, MessageContext context, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing order {OrderId} for customer {CustomerId}, amount {Amount:C}",
            message.OrderId, message.CustomerId, message.Amount);

        // Simulate work
        await Task.Delay(100, ct);

        _logger.LogInformation("Completed order {OrderId}", message.OrderId);
        // MessageContext.CompleteAsync is called by the library after HandleAsync returns without exception
    }
}
