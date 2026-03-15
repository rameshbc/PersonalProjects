using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AspireContainerStarter.Infrastructure.KeyVault.Extensions;

public static class KeyVaultExtensions
{
    /// <summary>
    /// Adds Azure Key Vault as a configuration provider using Managed Identity
    /// (DefaultAzureCredential — MI in Azure, Azure CLI / Visual Studio locally).
    ///
    /// Secrets are exposed as configuration keys using the Key Vault secret name,
    /// with double-dash (<c>--</c>) replaced by colon (<c>:</c>) so they map to
    /// nested configuration sections (e.g. secret <c>ConnectionStrings--Db</c>
    /// becomes <c>ConnectionStrings:Db</c>).
    ///
    /// The vault URI is resolved in priority order:
    ///   1. The explicit <paramref name="vaultUri"/> parameter.
    ///   2. The <c>ConnectionStrings__key-vault</c> value injected by Aspire
    ///      when the AppHost wires up <c>WithReference(keyVault)</c>.
    ///
    /// When neither is present the method is a no-op, allowing services to run
    /// locally without a Key Vault instance (falling back to appsettings and
    /// user secrets).
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="vaultUri">
    ///   Optional Key Vault URI (e.g. <c>https://my-vault.vault.azure.net/</c>).
    /// </param>
    /// <param name="reloadInterval">
    ///   When set, all secrets are reloaded at this interval.
    ///   Null (default) disables periodic reload; secrets are read once at startup.
    /// </param>
    /// <param name="manager">
    ///   Optional <see cref="KeyVaultSecretManager"/> to control which secrets are
    ///   included and how their names map to configuration keys.
    ///   Defaults to <see cref="KeyVaultSecretManager"/> (loads all enabled secrets).
    /// </param>
    public static IHostApplicationBuilder AddAzureKeyVaultWithManagedIdentity(
        this IHostApplicationBuilder builder,
        string? vaultUri = null,
        TimeSpan? reloadInterval = null,
        KeyVaultSecretManager? manager = null)
    {
        var resolvedUri = vaultUri
            ?? builder.Configuration.GetConnectionString("key-vault");

        // No URI = local dev without Key Vault; fall back to local config.
        if (resolvedUri is null)
            return builder;

        var credential = new DefaultAzureCredential();
        var secretClient = new SecretClient(new Uri(resolvedUri), credential);

        builder.Configuration.AddAzureKeyVault(
            secretClient,
            new AzureKeyVaultConfigurationOptions
            {
                Manager = manager ?? new KeyVaultSecretManager(),
                ReloadInterval = reloadInterval,
            });

        return builder;
    }

}
