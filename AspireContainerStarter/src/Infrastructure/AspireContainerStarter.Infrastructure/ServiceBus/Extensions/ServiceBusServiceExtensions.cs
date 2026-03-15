using AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;
using AspireContainerStarter.Infrastructure.ServiceBus.Implementations;
using AspireContainerStarter.Infrastructure.ServiceBus.Monitoring;
using AspireContainerStarter.Infrastructure.ServiceBus.Options;
using AspireContainerStarter.Infrastructure.ServiceBus.Resilience;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Polly;

namespace AspireContainerStarter.Infrastructure.ServiceBus.Extensions;

/// <summary>
/// Extension methods for registering Azure Service Bus with Managed Identity auth.
///
/// Usage — producer (API, workers that publish):
/// <code>
///   builder.Services.AddAzureServiceBusPublisherWithManagedIdentity(
///       fullyQualifiedNamespace: "my-ns.servicebus.windows.net",
///       queueOrTopicName: "fed-calculations");
/// </code>
///
/// Usage — consumer (workers that receive):
/// <code>
///   builder.Services.AddAzureServiceBusConsumerWithManagedIdentity&lt;FedMessage, FedHandler&gt;(
///       fullyQualifiedNamespace: "my-ns.servicebus.windows.net",
///       queueName: "fed-calculations");
/// </code>
/// </summary>
public static class ServiceBusServiceExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="ServiceBusClient"/> authenticated
    /// via DefaultAzureCredential, plus an <see cref="IMessagePublisher"/>
    /// for the specified queue or topic.
    ///
    /// <para>
    /// When <paramref name="serviceKey"/> is supplied the publisher is registered
    /// as a <em>keyed</em> singleton — inject with
    /// <c>[FromKeyedServices("key")]</c> in minimal-API handlers.
    /// When <paramref name="serviceKey"/> is <see langword="null"/> (default) it
    /// is registered as a plain singleton (backward-compatible).
    /// </para>
    /// </summary>
    public static IServiceCollection AddAzureServiceBusPublisherWithManagedIdentity(
        this IServiceCollection services,
        string fullyQualifiedNamespace,
        string queueOrTopicName,
        string? serviceKey = null,
        Action<ServiceBusResilienceOptions>? configureResilience = null,
        Action<ServiceBusClientOptions>? configureClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullyQualifiedNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueOrTopicName);

        var resilienceOptions = new ServiceBusResilienceOptions();
        configureResilience?.Invoke(resilienceOptions);

        // Build the Polly pipeline at registration time.
        var pipelineBuilder = new ResiliencePipelineBuilder();
        ServiceBusResiliencePipeline.Configure(pipelineBuilder, resilienceOptions);
        var pipeline = pipelineBuilder.Build();

        RegisterSharedClient(services, fullyQualifiedNamespace, configureClient);

        if (serviceKey is null)
        {
            // Plain singleton — backward-compatible default.
            services.AddSingleton<IMessagePublisher>(sp =>
            {
                var client = sp.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(queueOrTopicName);
                return ActivatorUtilities.CreateInstance<ServiceBusMessagePublisher>(sp, sender, pipeline);
            });
        }
        else
        {
            // Keyed singleton — inject with [FromKeyedServices(serviceKey)].
            services.AddKeyedSingleton<IMessagePublisher>(serviceKey, (sp, _) =>
            {
                var client = sp.GetRequiredService<ServiceBusClient>();
                var sender = client.CreateSender(queueOrTopicName);
                return ActivatorUtilities.CreateInstance<ServiceBusMessagePublisher>(sp, sender, pipeline);
            });
        }

        AddHealthCheck(services, fullyQualifiedNamespace, queueOrTopicName);

        return services;
    }

    /// <summary>
    /// Registers a <see cref="ServiceBusProcessor"/> and wires it to
    /// <typeparamref name="TConsumer"/> as a hosted background receiver.
    /// </summary>
    public static IServiceCollection AddAzureServiceBusConsumerWithManagedIdentity<TMessage, TConsumer>(
        this IServiceCollection services,
        string fullyQualifiedNamespace,
        string queueName,
        Action<ServiceBusProcessorOptions>? configureProcessor = null,
        Action<ServiceBusClientOptions>? configureClient = null)
        where TMessage : class
        where TConsumer : class, IMessageConsumer<TMessage>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullyQualifiedNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        RegisterSharedClient(services, fullyQualifiedNamespace, configureClient);

        services.AddScoped<IMessageConsumer<TMessage>, TConsumer>();

        services.AddSingleton<ServiceBusProcessor>(sp =>
        {
            var client = sp.GetRequiredService<ServiceBusClient>();
            var processorOptions = new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls   = 4,
                AutoCompleteMessages = false    // Manual complete for exactly-once semantics.
            };
            configureProcessor?.Invoke(processorOptions);
            return client.CreateProcessor(queueName, processorOptions);
        });

        AddHealthCheck(services, fullyQualifiedNamespace, queueName);

        return services;
    }

    /// <summary>
    /// Registers an <see cref="AdaptiveServiceBusProcessorHostedService{TMessage}"/>
    /// that dynamically adjusts in-process concurrency as queue depth changes.
    ///
    /// Also registers <see cref="ServiceBusAdministrationClient"/> (singleton, MI auth)
    /// and <see cref="QueueDepthMonitor"/> (singleton) used by the monitor loop.
    ///
    /// Usage:
    /// <code>
    ///   builder.Services.AddAdaptiveAzureServiceBusConsumerWithManagedIdentity&lt;Calc1JobMessage, Calc1JobWorker&gt;(
    ///       fullyQualifiedNamespace: "my-ns.servicebus.windows.net",
    ///       queueName: "calc1-jobs",
    ///       configureConcurrency: opts =>
    ///       {
    ///           opts.MinConcurrency = 2;
    ///           opts.MaxConcurrency = 20;
    ///           opts.GrowthThreshold = 10;
    ///       });
    /// </code>
    /// </summary>
    public static IServiceCollection AddAdaptiveAzureServiceBusConsumerWithManagedIdentity<TMessage, TConsumer>(
        this IServiceCollection services,
        string fullyQualifiedNamespace,
        string queueName,
        Action<AdaptiveConcurrencyOptions>? configureConcurrency = null,
        Action<ServiceBusProcessorOptions>? configureProcessor = null,
        Action<ServiceBusClientOptions>? configureClient = null)
        where TMessage : class
        where TConsumer : class, IMessageConsumer<TMessage>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullyQualifiedNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        RegisterSharedClient(services, fullyQualifiedNamespace, configureClient);

        // ServiceBusAdministrationClient — used by QueueDepthMonitor.
        if (!services.Any(d => d.ServiceType == typeof(ServiceBusAdministrationClient)))
        {
            services.AddSingleton(_ =>
                fullyQualifiedNamespace.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase)
                    ? new ServiceBusAdministrationClient(fullyQualifiedNamespace)
                    : new ServiceBusAdministrationClient(fullyQualifiedNamespace, new DefaultAzureCredential()));
        }

        services.AddSingleton<QueueDepthMonitor>();

        // Configure and store options (including the internal QueueName).
        services.Configure<AdaptiveConcurrencyOptions>(opts =>
        {
            configureConcurrency?.Invoke(opts);
            opts.QueueName = queueName;
        });

        services.AddScoped<IMessageConsumer<TMessage>, TConsumer>();

        services.AddSingleton<ServiceBusProcessor>(sp =>
        {
            var client = sp.GetRequiredService<ServiceBusClient>();
            var options = sp.GetRequiredService<IOptions<AdaptiveConcurrencyOptions>>().Value;
            var processorOptions = new ServiceBusProcessorOptions
            {
                // Allow up to MaxConcurrency deliveries; the gate limits real concurrency.
                MaxConcurrentCalls   = options.MaxConcurrency,
                AutoCompleteMessages = false
            };
            configureProcessor?.Invoke(processorOptions);
            return client.CreateProcessor(queueName, processorOptions);
        });

        services.AddHostedService<AdaptiveServiceBusProcessorHostedService<TMessage>>();

        AddHealthCheck(services, fullyQualifiedNamespace, queueName);

        return services;
    }

    /// <summary>
    /// Registers a <see cref="ServiceBusProcessor"/> for a topic <em>subscription</em>
    /// and wires it to <typeparamref name="TConsumer"/> as a hosted background receiver.
    ///
    /// <para>
    /// After calling this, add the hosted service in the consuming service's
    /// <c>Program.cs</c>:
    /// </para>
    /// <code>
    ///   builder.Services.AddHostedService&lt;ServiceBusProcessorHostedService&lt;JobProgressMessage&gt;&gt;();
    /// </code>
    /// </summary>
    public static IServiceCollection AddAzureServiceBusTopicConsumerWithManagedIdentity<TMessage, TConsumer>(
        this IServiceCollection services,
        string fullyQualifiedNamespace,
        string topicName,
        string subscriptionName,
        Action<ServiceBusProcessorOptions>? configureProcessor = null,
        Action<ServiceBusClientOptions>? configureClient = null)
        where TMessage : class
        where TConsumer : class, IMessageConsumer<TMessage>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullyQualifiedNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionName);

        RegisterSharedClient(services, fullyQualifiedNamespace, configureClient);

        services.AddScoped<IMessageConsumer<TMessage>, TConsumer>();

        services.AddSingleton<ServiceBusProcessor>(sp =>
        {
            var client = sp.GetRequiredService<ServiceBusClient>();
            var processorOptions = new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls   = 4,
                AutoCompleteMessages = false
            };
            configureProcessor?.Invoke(processorOptions);
            // Topic subscription processor — differs from queue processor only in ctor overload.
            return client.CreateProcessor(topicName, subscriptionName, processorOptions);
        });

        AddTopicSubscriptionHealthCheck(services, fullyQualifiedNamespace, topicName, subscriptionName);

        return services;
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers <see cref="ServiceBusClient"/> as a singleton once.
    /// Safe to call multiple times — guarded by a type-presence check.
    ///
    /// <para>
    /// Aspire injects two formats depending on the environment:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Local dev (emulator)</b>: full connection string
    ///     <c>Endpoint=sb://localhost;SharedAccessKeyName=...;SharedAccessKey=...</c>
    ///     — use <see cref="ServiceBusClient(string, ServiceBusClientOptions)"/>
    ///     with default AMQP-TCP transport (emulator doesn't expose WebSocket port).
    ///   </description></item>
    ///   <item><description>
    ///     <b>Azure (publish mode)</b>: namespace FQDN
    ///     <c>my-ns.servicebus.windows.net</c>
    ///     — use <see cref="ServiceBusClient(string, Azure.Core.TokenCredential, ServiceBusClientOptions)"/>
    ///     with <see cref="DefaultAzureCredential"/> and AMQP-WebSockets for firewall traversal.
    ///   </description></item>
    /// </list>
    /// </para>
    /// </summary>
    private static void RegisterSharedClient(
        IServiceCollection services,
        string fullyQualifiedNamespace,
        Action<ServiceBusClientOptions>? configureClient)
    {
        if (services.Any(d => d.ServiceType == typeof(ServiceBusClient)))
            return;

        services.AddSingleton(_ =>
        {
            // Aspire emulator / SAS connection strings start with "Endpoint=".
            // Azure MI auth uses just the namespace FQDN (no "Endpoint=" prefix).
            if (fullyQualifiedNamespace.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
            {
                // Local dev emulator or explicit SAS connection string.
                // Use default AmqpTcp transport — emulator doesn't expose WebSocket port.
                var opts = new ServiceBusClientOptions();
                configureClient?.Invoke(opts);
                return new ServiceBusClient(fullyQualifiedNamespace, opts);
            }

            // Azure: namespace FQDN + Managed Identity + WebSockets (firewall-friendly).
            var clientOptions = new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpWebSockets
            };
            configureClient?.Invoke(clientOptions);
            return new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential(), clientOptions);
        });
    }

    private static void AddHealthCheck(
        IServiceCollection services,
        string fullyQualifiedNamespace,
        string entityName)
    {
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                $"servicebus-{entityName}",
                sp => new ServiceBusEntityHealthCheck(
                    sp.GetRequiredService<ServiceBusClient>(), entityName, subscriptionName: null),
                HealthStatus.Degraded,
                ["messaging", "servicebus", "azure"]));
    }

    private static void AddTopicSubscriptionHealthCheck(
        IServiceCollection services,
        string fullyQualifiedNamespace,
        string topicName,
        string subscriptionName)
    {
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                $"servicebus-{topicName}-{subscriptionName}",
                sp => new ServiceBusEntityHealthCheck(
                    sp.GetRequiredService<ServiceBusClient>(), topicName, subscriptionName),
                HealthStatus.Degraded,
                ["messaging", "servicebus", "azure"]));
    }
}

/// <summary>
/// Lightweight health check that verifies a Service Bus queue or topic subscription
/// is reachable by peeking one message (non-destructive).
/// </summary>
file sealed class ServiceBusEntityHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly ServiceBusClient _client;
    private readonly string _entityPath;
    private readonly string? _subscriptionName;

    public ServiceBusEntityHealthCheck(ServiceBusClient client, string entityPath, string? subscriptionName)
    {
        _client           = client;
        _entityPath       = entityPath;
        _subscriptionName = subscriptionName;
    }

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            var receiver = _subscriptionName is null
                ? _client.CreateReceiver(_entityPath)
                : _client.CreateReceiver(_entityPath, _subscriptionName);
            await receiver.PeekMessageAsync(cancellationToken: ct);
            await receiver.DisposeAsync();
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
