using AspireContainerStarter.Infrastructure.SqlServer.Interceptors;
using AspireContainerStarter.Infrastructure.SqlServer.Resilience;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;

namespace AspireContainerStarter.Infrastructure.SqlServer.Extensions;

/// <summary>
/// Extension methods for registering Azure SQL with Managed Identity auth,
/// EF Core, health checks, and a Polly resilience pipeline.
///
/// Usage (in any service's Program.cs or Startup):
/// <code>
///   builder.Services.AddAzureSqlWithManagedIdentity&lt;MyDbContext&gt;(
///       connectionString: builder.Configuration.GetConnectionString("SqlDb")!,
///       configure: opt => opt.MaxRetryAttempts = 3);
/// </code>
/// </summary>
public static class SqlServerServiceExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> with Azure SQL using
    /// DefaultAzureCredential (Managed Identity in Azure, CLI/VS locally).
    /// </summary>
    public static IServiceCollection AddAzureSqlWithManagedIdentity<TContext>(
        this IServiceCollection services,
        string connectionString,
        Action<SqlServerResilienceOptions>? configureResilience = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        string healthCheckName = "azure-sql")
        where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var resilienceOptions = new SqlServerResilienceOptions();
        configureResilience?.Invoke(resilienceOptions);

        // Build the Polly resilience pipeline at registration time.
        var pipelineBuilder = new ResiliencePipelineBuilder();
        SqlServerResiliencePipeline.Configure(pipelineBuilder, resilienceOptions);
        var pipeline = pipelineBuilder.Build();

        // Register the Azure AD token interceptor as a singleton — the credential
        // is thread-safe and caches tokens internally.
        services.AddSingleton<AzureAdTokenInterceptor>(
            _ => new AzureAdTokenInterceptor(new DefaultAzureCredential()));

        // Register the resilience pipeline — consumed by higher-level orchestration if needed.
        services.AddKeyedSingleton<ResiliencePipeline>("azure-sql", (_, _) => pipeline);

        // Register EF Core DbContext with the interceptor injected.
        services.AddDbContext<TContext>((sp, options) =>
        {
            var interceptor = sp.GetRequiredService<AzureAdTokenInterceptor>();

            options.UseSqlServer(connectionString, sqlOptions =>
            {
                // Disable EF internal retry — Polly handles retry at higher level.
                sqlOptions.EnableRetryOnFailure(0);
                sqlOptions.CommandTimeout(60);
            });

            options.AddInterceptors(interceptor);

            configureDbContext?.Invoke(options);
        });

        // Health check — validates the SQL connection is reachable.
        services.AddHealthChecks()
            .AddSqlServer(
                connectionString: connectionString,
                name: healthCheckName,
                failureStatus: HealthStatus.Degraded,
                tags: ["db", "sql", "azure"]);

        return services;
    }
}
