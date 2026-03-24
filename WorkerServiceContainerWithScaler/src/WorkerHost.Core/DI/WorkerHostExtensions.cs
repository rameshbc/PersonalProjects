namespace WorkerHost.Core.DI;

using Messaging.Core.Models;
using Messaging.Core.Receivers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class WorkerHostExtensions
{
    /// <summary>
    /// Registers a typed message handler as an IHostedService.
    /// TWorker must extend MessageHandlerHostedService&lt;TMessage&gt;.
    /// </summary>
    public static IServiceCollection AddMessageHandler<TMessage, THandler, TWorker>(
        this IServiceCollection services,
        string destinationName,
        Action<ReceiveOptions>? configureReceive = null)
        where TMessage  : class
        where THandler  : class, Messaging.Core.Abstractions.IMessageHandler<TMessage>
        where TWorker   : MessageHandlerHostedService<TMessage>
    {
        services.AddSingleton<Messaging.Core.Abstractions.IMessageHandler<TMessage>, THandler>();

        var receiveOpts = new ReceiveOptions();
        configureReceive?.Invoke(receiveOpts);

        services.AddHostedService(sp =>
        {
            var receiver = sp.GetRequiredService<MessageReceiver>();
            var handler  = sp.GetRequiredService<Messaging.Core.Abstractions.IMessageHandler<TMessage>>();
            var logger   = sp.GetRequiredService<ILogger<TWorker>>();
            return (TWorker)Activator.CreateInstance(typeof(TWorker), receiver, handler, destinationName, receiveOpts, logger)!;
        });

        return services;
    }
}
