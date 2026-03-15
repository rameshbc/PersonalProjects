using AspireContainerStarter.Infrastructure.Redis.Resilience;
using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Polly;
using StackExchange.Redis;

namespace AspireContainerStarter.Infrastructure.Redis.Extensions;

/// <summary>
/// Extension methods for registering Azure Cache for Redis with
/// Managed Identity (Entra ID) authentication.
///
/// Uses <c>Microsoft.Azure.StackExchangeRedis</c> v2+ to configure the
/// StackExchange.Redis connection with a token obtained from
/// <see cref="DefaultAzureCredential"/> — no password/access-key required.
///
/// Usage:
/// <code>
///   builder.Services.AddAzureRedisCacheWithManagedIdentity(
///       redisHostName: "my-cache.redis.cache.windows.net",
///       // principalId is the Object (principal) ID of the managed identity.
///       // Retrieve from Azure Portal → Managed Identity → Overview → Object ID.
///       principalId: builder.Configuration["ManagedIdentity:ObjectId"]);
/// </code>
/// </summary>
public static class RedisServiceExtensions
{
    private const string ResiliencePipelineKey = "azure-redis";

    public static IServiceCollection AddAzureRedisCacheWithManagedIdentity(
        this IServiceCollection services,
        string redisHostName,
        string? principalId = null,
        int    redisPort = 6380,
        Action<RedisResilienceOptions>? configureResilience = null,
        string healthCheckName = "azure-redis")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redisHostName);

        var resilienceOptions = new RedisResilienceOptions();
        configureResilience?.Invoke(resilienceOptions);

        // Build the Polly pipeline at registration time.
        var pipelineBuilder = new ResiliencePipelineBuilder();
        RedisResiliencePipeline.Configure(pipelineBuilder, resilienceOptions);
        var pipeline = pipelineBuilder.Build();
        services.AddKeyedSingleton<ResiliencePipeline>(ResiliencePipelineKey, (_, _) => pipeline);

        // Register IConnectionMultiplexer as singleton — designed to be long-lived and shared.
        // ConfigureForAzureWithTokenCredentialAsync (v2.x) handles token refresh automatically.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var logger     = sp.GetRequiredService<ILogger<IConnectionMultiplexer>>();
            var credential = new DefaultAzureCredential();

            var configOptions = new ConfigurationOptions
            {
                EndPoints            = { { redisHostName, redisPort } },
                Ssl                  = true,
                AbortOnConnectFail   = false,
                ConnectRetry         = 3,
                ReconnectRetryPolicy = new LinearRetry((int)TimeSpan.FromSeconds(5).TotalMilliseconds)
            };

            // Microsoft.Azure.StackExchangeRedis v2.x API:
            // principalId = Object (principal) ID of the managed identity.
            // In dev (no MI), an empty string causes fallback to password-based auth from the connection string.
            var resolvedPrincipalId = principalId ?? string.Empty;
            AzureCacheForRedis.ConfigureForAzureWithTokenCredentialAsync(
                configOptions, resolvedPrincipalId, credential)
                .GetAwaiter().GetResult();

            var multiplexer = ConnectionMultiplexer.Connect(configOptions);
            multiplexer.ConnectionFailed  += (_, e) =>
                logger.LogWarning("Redis connection failed: {Failure}", e.FailureType);
            multiplexer.ConnectionRestored += (_, _) =>
                logger.LogInformation("Redis connection restored");

            return multiplexer;
        });

        // Register IDistributedCache backed by the singleton multiplexer.
        services.AddSingleton<IDistributedCache>(sp =>
        {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
            var cacheOptions = new RedisCacheOptions
            {
                ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer),
                InstanceName = $"{redisHostName}:"
            };
            return new RedisCache(cacheOptions);
        });

        // Health check using the registered multiplexer.
        services.AddHealthChecks()
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: healthCheckName,
                failureStatus: HealthStatus.Degraded,
                tags: ["cache", "redis", "azure"]);

        return services;
    }
}
