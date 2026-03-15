using System.Text.Json;
using AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AspireContainerStarter.Infrastructure.ServiceBus.Implementations;

/// <summary>
/// Generic hosted service that starts a <see cref="ServiceBusProcessor"/>
/// and dispatches each received message to a scoped
/// <see cref="IMessageConsumer{T}"/> implementation.
///
/// Register via <c>AddAzureServiceBusConsumerWithManagedIdentity</c> in the
/// Infrastructure extensions, then add this hosted service in each worker's
/// Program.cs:
/// <code>
///   builder.Services.AddHostedService&lt;ServiceBusProcessorHostedService&lt;FedCalculationMessage&gt;&gt;();
/// </code>
/// </summary>
public sealed class ServiceBusProcessorHostedService<TMessage> : IHostedService, IAsyncDisposable
    where TMessage : class
{
    private readonly ServiceBusProcessor _processor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ServiceBusProcessorHostedService<TMessage>> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ServiceBusProcessorHostedService(
        ServiceBusProcessor processor,
        IServiceScopeFactory scopeFactory,
        ILogger<ServiceBusProcessorHostedService<TMessage>> logger)
    {
        _processor   = processor;
        _scopeFactory = scopeFactory;
        _logger      = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync   += HandleErrorAsync;
        await _processor.StartProcessingAsync(cancellationToken);
        _logger.LogInformation("Service Bus processor started for {MessageType}", typeof(TMessage).Name);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _processor.StopProcessingAsync(cancellationToken);
        _logger.LogInformation("Service Bus processor stopped for {MessageType}", typeof(TMessage).Name);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId     = args.Message.MessageId;
        var correlationId = args.Message.CorrelationId;

        try
        {
            var payload = args.Message.Body.ToObjectFromJson<TMessage>(_jsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialise {typeof(TMessage).Name}");

            await using var scope = _scopeFactory.CreateAsyncScope();
            var consumer = scope.ServiceProvider.GetRequiredService<IMessageConsumer<TMessage>>();
            await consumer.HandleAsync(payload, messageId, correlationId, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            _logger.LogDebug("Completed message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {MessageId} of type {Type}",
                messageId, typeof(TMessage).Name);

            // Dead-letter after exceeding delivery count — do not abandon here
            // so the broker can apply its configured retry policy.
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus error on {EntityPath}: {ErrorSource}",
            args.EntityPath, args.ErrorSource);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _processor.DisposeAsync();
}
