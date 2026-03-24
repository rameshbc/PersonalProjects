namespace SampleWorker.Queue.Workers;

using Messaging.Core.Models;
using Messaging.Core.Receivers;
using Microsoft.Extensions.Logging;
using WorkerHost.Core;
using Messaging.Core.Abstractions;
using SampleWorker.Queue.Messages;

public sealed class OrderQueueWorker : MessageHandlerHostedService<OrderCreatedMessage>
{
    public OrderQueueWorker(
        MessageReceiver receiver,
        IMessageHandler<OrderCreatedMessage> handler,
        string destinationName,
        ReceiveOptions receiveOptions,
        ILogger<OrderQueueWorker> logger)
        : base(receiver, handler, destinationName, receiveOptions, logger) { }
}
