using AspireContainerStarter.Calc2Worker.Workers;
using AspireContainerStarter.Contracts.Messages;
using AspireContainerStarter.Infrastructure.AppConfig.Extensions;
using AspireContainerStarter.Infrastructure.KeyVault.Extensions;
using AspireContainerStarter.Infrastructure.Redis.Extensions;
using AspireContainerStarter.Infrastructure.ServiceBus.Extensions;
using AspireContainerStarter.Infrastructure.ServiceBus.Implementations;
using AspireContainerStarter.Infrastructure.SqlServer.Data;
using AspireContainerStarter.Infrastructure.SqlServer.Extensions;
using AspireContainerStarter.Infrastructure.SqlServer.Migrations;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// ─── Aspire service defaults ──────────────────────────────────────────────
builder.AddServiceDefaults();

// ─── Configuration providers ──────────────────────────────────────────────
// Both are no-ops locally (no connection string → graceful skip).
// In Azure, Aspire injects endpoints via WithReference() in the AppHost.
builder.AddAzureAppConfigurationWithManagedIdentity();
builder.AddAzureKeyVaultWithManagedIdentity();

// ─── Azure Redis Cache (MI auth) ──────────────────────────────────────────
var redisHost = builder.Configuration["ConnectionStrings:redis-cache"]
    ?? builder.Configuration["Redis:HostName"]
    ?? "localhost";

if (!redisHost.Contains("localhost", StringComparison.OrdinalIgnoreCase)
    && !redisHost.Contains("127.0.0.1"))
{
    builder.Services.AddAzureRedisCacheWithManagedIdentity(redisHost);
}
else
{
    builder.Services.AddStackExchangeRedisCache(opt => opt.Configuration = redisHost);
}

// ─── Azure SQL / EF Core ──────────────────────────────────────────────────
// Local dev: plain AddDbContext (SQL auth); Azure: MI auth via interceptor.
var sqlCs = builder.Configuration.GetConnectionString("calculations-db");
if (!string.IsNullOrWhiteSpace(sqlCs))
{
    if (sqlCs.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
        sqlCs.Contains("127.0.0.1"))
    {
        builder.Services.AddDbContext<CalculationDbContext>(o =>
            o.UseSqlServer(sqlCs, sql => sql.CommandTimeout(60)));
    }
    else
    {
        builder.Services.AddAzureSqlWithManagedIdentity<CalculationDbContext>(sqlCs);
    }

    builder.Services.AddHostedService<DbMigratorHostedService<CalculationDbContext>>();
}

// ─── Azure Service Bus (MI auth) ──────────────────────────────────────────
// Consumer: reads Calc2JobMessage from the calc2-jobs queue.
var sbNamespace = builder.Configuration["ConnectionStrings:service-bus"]
    ?? builder.Configuration["ServiceBus:FullyQualifiedNamespace"]!;

builder.Services.AddAzureServiceBusConsumerWithManagedIdentity<
    Calc2JobMessage,
    Calc2JobWorker>(
    fullyQualifiedNamespace: sbNamespace,
    queueName: builder.Configuration["ServiceBus:Calc2Queue"] ?? "calc2-jobs");

// Publisher: sends JobProgressMessage to the job-progress topic.
builder.Services.AddAzureServiceBusPublisherWithManagedIdentity(
    fullyQualifiedNamespace: sbNamespace,
    queueOrTopicName: builder.Configuration["ServiceBus:ProgressTopic"] ?? "job-progress");

// ─── Background processor ─────────────────────────────────────────────────
builder.Services.AddHostedService<ServiceBusProcessorHostedService<Calc2JobMessage>>();

var host = builder.Build();
await host.RunAsync();
