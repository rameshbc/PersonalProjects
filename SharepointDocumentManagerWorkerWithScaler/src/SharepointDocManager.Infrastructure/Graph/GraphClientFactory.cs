using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace SharepointDocManager.Infrastructure.Graph;

/// <summary>
/// Creates and provides a singleton <see cref="GraphServiceClient"/>.
///
/// Auth strategy (controlled by GRAPH__AUTHMODE environment variable):
///
///   "ManagedIdentity"    — Production. No secrets. Compute must have MI assigned.
///                          User-Assigned MI: set AZURE_CLIENT_ID to the MI's client ID.
///                          System-Assigned MI: leave AZURE_CLIENT_ID unset.
///
///   "ClientCredentials"  — Local dev / CI bootstrap only.
///                          Set AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET.
///
///   (anything else)      — DefaultAzureCredential chain: MI → AzureCLI → VS → env vars.
///                          Good for developer workstations.
///
/// Graph SDK v5 handles token caching and proactive renewal internally.
/// The returned client is thread-safe and is registered as a singleton in DI.
/// </summary>
public sealed class GraphClientFactory
{
    // Application-level Graph scopes for app-only access.
    // The MI app registration must have these granted with admin consent.
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    private readonly ILogger<GraphClientFactory> _logger;
    private readonly string _authMode;

    public GraphClientFactory(IConfiguration config, ILogger<GraphClientFactory> logger)
    {
        _logger   = logger;
        _authMode = config["Graph:AuthMode"] ?? "Default";
    }

    public GraphServiceClient Create()
    {
        var credential = _authMode switch
        {
            "ManagedIdentity"   => BuildManagedIdentity(),
            "ClientCredentials" => BuildClientCredentials(),
            _                   => BuildDefault()
        };

        _logger.LogInformation("GraphClientFactory: initialised with auth mode '{Mode}'.", _authMode);
        return new GraphServiceClient(credential, Scopes);
    }

    // Uses Azure IMDS. Works on App Service, Azure VM, AKS with MI assigned.
    private static TokenCredential BuildManagedIdentity()
    {
        var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        return string.IsNullOrWhiteSpace(clientId)
            ? new ManagedIdentityCredential()
            : new ManagedIdentityCredential(new ResourceIdentifier(clientId));
    }

    // Service principal with secret. NEVER use in production — secrets rotate and expire.
    private static TokenCredential BuildClientCredentials() =>
        new ClientSecretCredential(
            tenantId:     Environment.GetEnvironmentVariable("AZURE_TENANT_ID")!,
            clientId:     Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")!,
            clientSecret: Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET")!);

    // Full credential chain — MI → AzureCLI → Visual Studio → environment variables.
    private static TokenCredential BuildDefault() => new DefaultAzureCredential();
}
