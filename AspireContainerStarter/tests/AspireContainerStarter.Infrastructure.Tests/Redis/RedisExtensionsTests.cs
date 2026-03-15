using AspireContainerStarter.Infrastructure.Redis.Extensions;
using AspireContainerStarter.Infrastructure.Redis.Resilience;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace AspireContainerStarter.Infrastructure.Tests.Redis;

public sealed class RedisExtensionsTests
{
    [Fact]
    public void AddAzureRedisCacheWithManagedIdentity_ThrowsOnEmptyHostName()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddAzureRedisCacheWithManagedIdentity(redisHostName: ""));
    }

    [Fact]
    public void AddAzureRedisCacheWithManagedIdentity_RegistersDistributedCache()
    {
        // We can only verify DI registration without a real Redis endpoint.
        // Use a fake hostname — the singleton connection is lazy, so no
        // actual TCP connection is attempted during registration.
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAzureRedisCacheWithManagedIdentity("my-cache.redis.cache.windows.net");

        // IDistributedCache should be registered.
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDistributedCache));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddAzureRedisCacheWithManagedIdentity_RegistersHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAzureRedisCacheWithManagedIdentity("my-cache.redis.cache.windows.net");

        var provider  = services.BuildServiceProvider();
        var hcService = provider.GetService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        Assert.NotNull(hcService);
    }

    [Fact]
    public void AddAzureRedisCacheWithManagedIdentity_ConfiguresResilienceOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        RedisResilienceOptions? captured = null;

        services.AddAzureRedisCacheWithManagedIdentity(
            redisHostName: "my-cache.redis.cache.windows.net",
            configureResilience: opts =>
            {
                opts.MaxRetryAttempts = 1;
                captured = opts;
            });

        Assert.NotNull(captured);
        Assert.Equal(1, captured.MaxRetryAttempts);
    }

    [Fact]
    public void RedisResiliencePipeline_DefaultOptions_AreReasonable()
    {
        var options = new RedisResilienceOptions();

        Assert.True(options.MaxRetryAttempts > 0);
        Assert.True(options.BaseRetryDelay > TimeSpan.Zero);
        Assert.True(options.CircuitBreakerFailureRatio is > 0 and < 1);
        Assert.True(options.CircuitBreakerBreakDuration > TimeSpan.Zero);
    }
}
