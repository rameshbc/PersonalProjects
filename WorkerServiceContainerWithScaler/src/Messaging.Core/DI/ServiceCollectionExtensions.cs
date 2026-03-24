namespace Messaging.Core.DI;

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Messaging.Core.Abstractions;
using Messaging.Core.Audit;
using Messaging.Core.Audit.DbContext;
using Messaging.Core.Audit.Repositories;
using Messaging.Core.Compression;
using Messaging.Core.Options;
using Messaging.Core.Publishers;
using Messaging.Core.Receivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all messaging services (publisher, receiver, resilience, compression, audit).
    /// A single call wires everything — no separate AddMessagingAudit needed.
    /// </summary>
    public static IServiceCollection AddServiceBusMessaging(
        this IServiceCollection services,
        Action<MessagingOptions> configure)
    {
        services.Configure(configure);

        // Resolve options eagerly so we can use them during registration
        var opts = new MessagingOptions();
        configure(opts);

        // ── Service Bus client (singleton, thread-safe) ──────────────────────
        services.AddSingleton(_ =>
        {
            if (opts.AuthMode == ServiceBusAuthMode.ManagedIdentity)
            {
                if (string.IsNullOrWhiteSpace(opts.FullyQualifiedNamespace))
                    throw new InvalidOperationException(
                        "MessagingOptions.FullyQualifiedNamespace must be set when AuthMode = ManagedIdentity.");

                var credential = opts.ManagedIdentityClientId is not null
                    ? (Azure.Core.TokenCredential)new ManagedIdentityCredential(opts.ManagedIdentityClientId)
                    : new DefaultAzureCredential();

                return new ServiceBusClient(opts.FullyQualifiedNamespace, credential);
            }

            if (string.IsNullOrWhiteSpace(opts.ConnectionString))
                throw new InvalidOperationException(
                    "MessagingOptions.ConnectionString must be set when AuthMode = ConnectionString.");

            return new ServiceBusClient(opts.ConnectionString);
        });

        // ── Compression ──────────────────────────────────────────────────────
        services.AddSingleton<IPayloadCompressor, GZipPayloadCompressor>();

        // ── Audit DB (EF Core pooled factory) ───────────────────────────────
        if (opts.Audit.Enabled)
        {
            if (string.IsNullOrWhiteSpace(opts.Audit.ConnectionString))
                throw new InvalidOperationException(
                    "AuditOptions.ConnectionString must be set when audit is enabled.");

            services.AddPooledDbContextFactory<MessagingAuditDbContext>(dbOpts =>
                dbOpts.UseSqlServer(opts.Audit.ConnectionString,
                    sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

            // Raw EF Core repository (used by AuditLogger internally)
            services.AddSingleton<EfCoreAuditRepository>();

            // AuditLogger wraps EfCoreAuditRepository with fire-and-forget Channel<T>
            // It is both IAuditRepository (injected into publisher/receiver)
            // and IHostedService (BackgroundService draining the channel)
            services.AddSingleton<AuditLogger>(sp =>
            {
                var inner  = sp.GetRequiredService<EfCoreAuditRepository>();
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditLogger>>();
                return new AuditLogger(inner, logger);
            });

            services.AddSingleton<IAuditRepository>(sp => sp.GetRequiredService<AuditLogger>());
            services.AddHostedService(sp => sp.GetRequiredService<AuditLogger>());
        }
        else
        {
            // No-op repository when audit is disabled
            services.AddSingleton<IAuditRepository, NullAuditRepository>();
        }

        // ── Publisher ────────────────────────────────────────────────────────
        services.AddSingleton<IMessagePublisher>(sp => new MessagePublisher(
            sp.GetRequiredService<ServiceBusClient>(),
            sp.GetRequiredService<IOptions<MessagingOptions>>().Value,
            sp.GetRequiredService<IPayloadCompressor>(),
            sp.GetRequiredService<IAuditRepository>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MessagePublisher>>()));

        // ── Receiver (transient — one per registration) ──────────────────────
        services.AddTransient(sp => new MessageReceiver(
            sp.GetRequiredService<ServiceBusClient>(),
            sp.GetRequiredService<IOptions<MessagingOptions>>().Value,
            sp.GetRequiredService<IAuditRepository>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MessageReceiver>>()));

        return services;
    }
}
