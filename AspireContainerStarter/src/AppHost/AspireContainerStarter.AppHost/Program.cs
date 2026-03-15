using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

bool isAzure = builder.ExecutionContext.IsPublishMode;

// ─── Azure App Configuration ──────────────────────────────────────────────
// Cloud-only: no local emulator. Services fall back to appsettings / user
// secrets when this reference is absent (local dev).
var appConfig = isAzure
    ? builder.AddAzureAppConfiguration("app-config")
    : null;

// ─── Azure Key Vault ──────────────────────────────────────────────────────
// Cloud-only: no local emulator. Services fall back to appsettings / user
// secrets when this reference is absent (local dev).
var keyVault = isAzure
    ? builder.AddAzureKeyVault("key-vault")
    : null;

// ─── Azure SQL ────────────────────────────────────────────────────────────
IResourceBuilder<IResourceWithConnectionString> sqlDb;

if (isAzure)
{
    // Provision Azure SQL; MI role assignments are wired by Aspire automatically.
    var sqlServer = builder.AddAzureSqlServer("sql-server");
    sqlDb = sqlServer.AddDatabase("calculations-db");
}
else
{
    // Local dev: SQL Server in Docker.
    var sqlServer = builder.AddSqlServer("sql-server", port: 1433)
                           .WithLifetime(ContainerLifetime.Persistent);
    sqlDb = sqlServer.AddDatabase("calculations-db");
}

// ─── Azure Cache for Redis ────────────────────────────────────────────────
IResourceBuilder<IResourceWithConnectionString> redis;

if (isAzure)
{
    redis = builder.AddAzureManagedRedis("redis-cache");
}
else
{
    redis = builder.AddRedis("redis-cache", port: 6379)
                   .WithLifetime(ContainerLifetime.Persistent);
}

// ─── Azure Service Bus ────────────────────────────────────────────────────
var serviceBus = builder.AddAzureServiceBus("service-bus");

if (!isAzure)
{
    // Local dev: Azure Service Bus Emulator running in Docker.
    // Image: mcr.microsoft.com/azure-messaging/servicebus-emulator (linux/amd64).
    // On Mac M1: Docker Desktop with Rosetta 2 emulation handles amd64 images
    // transparently — no extra configuration required.
    // Aspire reads the queue/topic declarations below and auto-generates the
    // emulator config JSON so no manual ServiceBusEmulatorConfig.json is needed.
    // Persistent: container survives AppHost restarts so workers don't wait for
    // the emulator to initialise on every run (same pattern as SQL/Redis).
    serviceBus.RunAsEmulator(emulator =>
        emulator.WithLifetime(ContainerLifetime.Persistent));
}

// Queues consumed by workers.
serviceBus.AddServiceBusQueue("calc1-jobs");
serviceBus.AddServiceBusQueue("calc2-jobs");

// Topic used by workers to broadcast progress; API subscribes via subscription.
var progressTopic = serviceBus.AddServiceBusTopic("job-progress");
progressTopic.AddServiceBusSubscription("api-progress-subscription");

// ─── Azure SignalR ────────────────────────────────────────────────────────
// In prod: Azure SignalR Service acts as the backplane for multi-instance API.
// In local dev: ASP.NET Core runs SignalR in-process.
var signalR = isAzure ? builder.AddAzureSignalR("signalr") : null;

// ─── API ──────────────────────────────────────────────────────────────────
var api = builder.AddProject<Projects.AspireContainerStarter_Api>("api")
    .WithReference(sqlDb)
    .WithReference(redis)
    .WithReference(serviceBus)
    .WithEnvironment("ServiceBus__ProgressTopic", "job-progress")
    .WithEnvironment("ServiceBus__ProgressSubscription", "api-progress-subscription");

if (appConfig is not null)
    api.WithReference(appConfig);

if (keyVault is not null)
    api.WithReference(keyVault);

if (signalR is not null)
    api.WithReference(signalR);

if (isAzure)
{
    // Container App: 1–5 replicas (scaling by HTTP concurrency via KEDA).
    // For custom KEDA rules (Service Bus queue length), add via
    // Azure Portal or Bicep under infra/ — see docs/scaling.md.
    api.PublishAsAzureContainerApp((_, containerApp) =>
    {
        containerApp.Template.Scale.MinReplicas = 1;
        containerApp.Template.Scale.MaxReplicas = 5;
    });
}

// ─── Calc1 Worker ─────────────────────────────────────────────────────────
var calc1Worker = builder.AddProject<Projects.AspireContainerStarter_Calc1Worker>("calc1-worker")
    .WithReference(sqlDb)
    .WithReference(redis)
    .WithReference(serviceBus)
    .WithEnvironment("ServiceBus__Calc1Queue", "calc1-jobs")
    .WithEnvironment("ServiceBus__ProgressTopic", "job-progress");

if (appConfig is not null)
    calc1Worker.WithReference(appConfig);

if (keyVault is not null)
    calc1Worker.WithReference(keyVault);

if (isAzure)
{
    // Scale 0–50 based on calc1-jobs queue length (KEDA).
    // KEDA rule metadata is configured via cd.yml az containerapp update step.
    calc1Worker.PublishAsAzureContainerApp((_, containerApp) =>
    {
        containerApp.Template.Scale.MinReplicas = 0;
        containerApp.Template.Scale.MaxReplicas = 50;
    });
}

// ─── Calc2 Worker ─────────────────────────────────────────────────────────
var calc2Worker = builder.AddProject<Projects.AspireContainerStarter_Calc2Worker>("calc2-worker")
    .WithReference(sqlDb)
    .WithReference(redis)
    .WithReference(serviceBus)
    .WithEnvironment("ServiceBus__Calc2Queue", "calc2-jobs")
    .WithEnvironment("ServiceBus__ProgressTopic", "job-progress");

if (appConfig is not null)
    calc2Worker.WithReference(appConfig);

if (keyVault is not null)
    calc2Worker.WithReference(keyVault);

if (isAzure)
{
    // Scale 0–50 based on calc2-jobs queue length (KEDA).
    calc2Worker.PublishAsAzureContainerApp((_, containerApp) =>
    {
        containerApp.Template.Scale.MinReplicas = 0;
        containerApp.Template.Scale.MaxReplicas = 50;
    });
}

await builder.Build().RunAsync();
