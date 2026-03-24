 using Messaging.Core.DI;
using Messaging.Core.Models;
using Microsoft.Extensions.Hosting;
using SampleWorker.Topic.Handlers;
using SampleWorker.Topic.Messages;
using SampleWorker.Topic.Workers;
using WorkerHost.Core.DI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddServiceBusMessaging(options =>
{
    builder.Configuration.GetSection("Messaging").Bind(options);
    options.Audit.ConnectionString = builder.Configuration.GetConnectionString("AuditDb") ?? string.Empty;
});

// Topic/subscription — destination is "order-events/shipping-sub"
builder.Services.AddMessageHandler<OrderShippedMessage, OrderShippedHandler, OrderShippedTopicWorker>(
    destinationName: "order-events/shipping-sub",
    configureReceive: opts =>
    {
        opts.ReceiveMode    = ReceiveMode.Push;
        opts.ProcessingMode = ProcessingMode.Sequential;
    });

await builder.Build().RunAsync();
