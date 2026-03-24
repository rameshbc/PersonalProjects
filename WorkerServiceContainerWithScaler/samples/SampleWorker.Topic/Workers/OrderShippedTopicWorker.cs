namespace SampleWorker.Topic.Workers;

using Messaging.Core.Abstractions;
using Messaging.Core.Models;
using Messaging.Core.Receivers;
using Microsoft.Extensions.Logging;
using SampleWorker.Topic.Messages;
using WorkerHost.Core;

public sealed class OrderShippedTopicWorker : MessageHandlerHostedService<OrderShippedMessage>
{
    public OrderShippedTopicWorker(
        MessageReceiver receiver,
        IMessageHandler<OrderShippedMessage> handler,
        string destinationName,
        ReceiveOptions receiveOptions,
        ILogger<OrderShippedTopicWorker> logger)
        : base(receiver, handler, destinationName, receiveOptions, logger) { }
}
