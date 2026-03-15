using AspireContainerStarter.Contracts.Messages;
using AspireContainerStarter.Infrastructure.ServiceBus.Abstractions;
using AspireContainerStarter.Infrastructure.ServiceBus.Extensions;
using AspireContainerStarter.Infrastructure.ServiceBus.Resilience;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;

namespace AspireContainerStarter.Infrastructure.Tests.ServiceBus;

public sealed class ServiceBusExtensionsTests
{
    // ─── Publisher ────────────────────────────────────────────────────────

    [Fact]
    public void AddAzureServiceBusPublisher_RegistersIMessagePublisher()
    {
        var services = BuildServices();

        services.AddAzureServiceBusPublisherWithManagedIdentity(
            fullyQualifiedNamespace: "test.servicebus.windows.net",
            queueOrTopicName: "test-queue");

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetService<IMessagePublisher>();

        Assert.NotNull(publisher);
    }

    [Fact]
    public void AddAzureServiceBusPublisher_ThrowsOnEmptyNamespace()
    {
        var services = BuildServices();

        Assert.Throws<ArgumentException>(() =>
            services.AddAzureServiceBusPublisherWithManagedIdentity(
                fullyQualifiedNamespace: "",
                queueOrTopicName: "test-queue"));
    }

    [Fact]
    public void AddAzureServiceBusPublisher_ThrowsOnEmptyQueueName()
    {
        var services = BuildServices();

        Assert.Throws<ArgumentException>(() =>
            services.AddAzureServiceBusPublisherWithManagedIdentity(
                fullyQualifiedNamespace: "test.servicebus.windows.net",
                queueOrTopicName: ""));
    }

    // ─── Consumer ─────────────────────────────────────────────────────────

    [Fact]
    public void AddAzureServiceBusConsumer_RegistersConsumer()
    {
        var services = BuildServices();

        services.AddAzureServiceBusConsumerWithManagedIdentity<
            Calc1JobMessage,
            FakeConsumer>(
            fullyQualifiedNamespace: "test.servicebus.windows.net",
            queueName: "fed-calculations");

        var provider  = services.BuildServiceProvider();
        var consumer  = provider.GetService<IMessageConsumer<Calc1JobMessage>>();

        Assert.NotNull(consumer);
        Assert.IsType<FakeConsumer>(consumer);
    }

    [Fact]
    public void AddAzureServiceBusConsumer_RegistersServiceBusProcessor()
    {
        var services = BuildServices();

        services.AddAzureServiceBusConsumerWithManagedIdentity<
            Calc1JobMessage,
            FakeConsumer>(
            fullyQualifiedNamespace: "test.servicebus.windows.net",
            queueName: "fed-calculations");

        var provider  = services.BuildServiceProvider();
        var processor = provider.GetService<ServiceBusProcessor>();

        Assert.NotNull(processor);
    }

    // ─── Resilience options ───────────────────────────────────────────────

    [Fact]
    public void AddAzureServiceBusPublisher_ConfiguresResilienceOptions()
    {
        var services = BuildServices();
        ServiceBusResilienceOptions? captured = null;

        services.AddAzureServiceBusPublisherWithManagedIdentity(
            fullyQualifiedNamespace: "test.servicebus.windows.net",
            queueOrTopicName: "test-queue",
            configureResilience: opts =>
            {
                opts.MaxRetryAttempts = 1;
                captured = opts;
            });

        Assert.NotNull(captured);
        Assert.Equal(1, captured.MaxRetryAttempts);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static ServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private sealed class FakeConsumer : IMessageConsumer<Calc1JobMessage>
    {
        public Task HandleAsync(Calc1JobMessage message, string messageId, string? correlationId, CancellationToken ct)
            => Task.CompletedTask;
    }
}
