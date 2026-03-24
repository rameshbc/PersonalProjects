namespace WorkerHost.Core;

using Messaging.Core.Abstractions;
using Messaging.Core.Models;
using Messaging.Core.Receivers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public abstract class MessageHandlerHostedService<T> : BackgroundService where T : class
{
    private readonly MessageReceiver _receiver;
    private readonly IMessageHandler<T> _handler;
    private readonly ReceiveOptions _receiveOptions;
    private readonly string _destinationName;
    private readonly ILogger _logger;

    protected MessageHandlerHostedService(
        MessageReceiver receiver,
        IMessageHandler<T> handler,
        string destinationName,
        ReceiveOptions receiveOptions,
        ILogger logger)
    {
        _receiver        = receiver;
        _handler         = handler;
        _destinationName = destinationName;
        _receiveOptions  = receiveOptions;
        _logger          = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Starting {HandlerType} on destination '{Destination}' (Mode={ReceiveMode}/{ProcessingMode})",
            typeof(T).Name, _destinationName,
            _receiveOptions.ReceiveMode, _receiveOptions.ProcessingMode);

        try
        {
            await _receiver.ReceiveAsync(_destinationName, _handler, _receiveOptions, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker stopping gracefully.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unhandled exception in {HandlerType}. Worker is stopping.", typeof(T).Name);
            throw;
        }
    }
}
