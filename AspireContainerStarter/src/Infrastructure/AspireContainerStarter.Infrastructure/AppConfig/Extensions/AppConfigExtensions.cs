using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Hosting;

namespace AspireContainerStarter.Infrastructure.AppConfig.Extensions;

public static class AppConfigExtensions
{
    /// <summary>
    /// Adds Azure App Configuration as a configuration provider using Managed Identity
    /// (DefaultAzureCredential — MI in Azure, Azure CLI / Visual Studio locally).
    ///
    /// The endpoint is resolved in priority order:
    ///   1. The explicit <paramref name="endpoint"/> parameter.
    ///   2. The <c>ConnectionStrings__app-config</c> value injected by Aspire
    ///      when the AppHost wires up <c>WithReference(appConfig)</c>.
    ///
    /// When neither is present the method is a no-op, allowing services to run
    /// locally without an App Configuration instance (falling back to appsettings
    /// and user secrets).
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="endpoint">
    ///   Optional App Configuration endpoint URL
    ///   (e.g. <c>https://mystore.azconfig.io</c>).
    /// </param>
    /// <param name="sentinelKey">
    ///   When set, changes to this key in App Configuration trigger a full
    ///   settings refresh. Requires <see cref="cacheExpiration"/> to take effect.
    /// </param>
    /// <param name="cacheExpiration">
    ///   Interval between refresh checks (passed to <c>SetRefreshInterval</c>).
    ///   Defaults to 30 seconds when <paramref name="sentinelKey"/> is provided.
    /// </param>
    /// <param name="label">
    ///   Label filter applied to all keys (e.g. the environment name).
    ///   Pass <c>null</c> (default) to load keys with no label.
    /// </param>
    /// <param name="configureOptions">
    ///   Optional delegate for additional <see cref="AzureAppConfigurationOptions"/>
    ///   configuration (feature flags, key prefixes, etc.).
    /// </param>
    public static IHostApplicationBuilder AddAzureAppConfigurationWithManagedIdentity(
        this IHostApplicationBuilder builder,
        string? endpoint = null,
        string? sentinelKey = null,
        TimeSpan? cacheExpiration = null,
        string? label = null,
        Action<AzureAppConfigurationOptions>? configureOptions = null)
    {
        var resolvedEndpoint = endpoint
            ?? builder.Configuration.GetConnectionString("app-config");

        // No endpoint = local dev without App Configuration; fall back to local config.
        if (resolvedEndpoint is null)
            return builder;

        var credential = new DefaultAzureCredential();

        builder.Configuration.AddAzureAppConfiguration(options =>
        {
            options.Connect(new Uri(resolvedEndpoint), credential);

            // Apply a label filter so services can load environment-specific values
            // (e.g. label = "production" loads keys tagged "production" in the store).
            if (label is not null)
                options.Select(KeyFilter.Any, label);

            // Wire up automatic refresh when a sentinel key is provided.
            if (sentinelKey is not null)
            {
                options.ConfigureRefresh(refresh =>
                    refresh
                        .Register(sentinelKey, refreshAll: true)
                        .SetRefreshInterval(cacheExpiration ?? TimeSpan.FromSeconds(30)));
            }

            configureOptions?.Invoke(options);
        });

        // Registers IConfigurationRefresherProvider and the refresh middleware so
        // the IConfigurationRefresher hosted service can push updates at runtime.
        builder.Services.AddAzureAppConfiguration();

        return builder;
    }
}
