using Messaging.Core.DI;
using Messaging.Core.Models;
using Microsoft.Extensions.Hosting;
using SampleWorker.Queue.Handlers;
using SampleWorker.Queue.Messages;
using SampleWorker.Queue.Workers;
using WorkerHost.Core.DI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddServiceBusMessaging(options =>
{
    // Bind non-secret settings from appsettings.json / appsettings.{Environment}.json.
    // Secrets (ConnectionString, FullyQualifiedNamespace) come from environment variables
    // or user-secrets — never from committed config files.
    //   Dev:  dotnet user-secrets set "Messaging:ConnectionString" "<emulator-cs>"
    //   Prod: env var  Messaging__FullyQualifiedNamespace=<your-ns>.servicebus.windows.net
    builder.Configuration.GetSection("Messaging").Bind(options);

    // Standard .NET connection string section — overridden by ConnectionStrings__AuditDb env var.
    options.Audit.ConnectionString = builder.Configuration.GetConnectionString("AuditDb") ?? string.Empty;
});

builder.Services.AddMessageHandler<OrderCreatedMessage, OrderCreatedHandler, OrderQueueWorker>(
    destinationName: "orders-queue",
    configureReceive: opts =>
    {
        opts.ReceiveMode            = ReceiveMode.PullBatch;
        opts.BatchSize              = 10;
        opts.ProcessingMode         = ProcessingMode.Parallel;
        opts.MaxDegreeOfParallelism = 4;
        opts.PrefetchCount          = 80;
    });

var host = builder.Build();
await host.RunAsync();
