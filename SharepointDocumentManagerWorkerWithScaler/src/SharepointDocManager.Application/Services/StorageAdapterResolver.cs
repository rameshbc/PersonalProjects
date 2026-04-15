using Microsoft.Extensions.DependencyInjection;
using SharepointDocManager.Core.Enums;
using SharepointDocManager.Core.Interfaces;

namespace SharepointDocManager.Application.Services;

/// <summary>
/// Resolves the correct IDocumentStorageAdapter for a client based on its StorageBackend.
///
/// This is the ONLY place in the entire codebase that branches on StorageBackend.
/// All callers above (services, handlers, controllers) are adapter-agnostic.
///
/// Both adapters are registered in DI with keyed services:
///   builder.Services.AddKeyedSingleton&lt;IDocumentStorageAdapter, SharePointAdapter&gt;("SP");
///   builder.Services.AddKeyedSingleton&lt;IDocumentStorageAdapter, SharePointEmbeddedAdapter&gt;("SPE");
///
/// StorageAdapterResolver is registered as a singleton — it holds no mutable state.
/// </summary>
public sealed class StorageAdapterResolver
{
    private readonly IClientSiteRepository _siteRepo;
    private readonly IServiceProvider      _services;

    public StorageAdapterResolver(IClientSiteRepository siteRepo, IServiceProvider services)
    {
        _siteRepo = siteRepo;
        _services = services;
    }

    /// <summary>
    /// Returns the IDocumentStorageAdapter registered for the client's backend.
    /// Throws if no ClientSite config exists for the given clientId.
    /// </summary>
    public async Task<IDocumentStorageAdapter> ResolveAsync(string clientId, CancellationToken ct)
    {
        var site = await _siteRepo.GetByClientIdAsync(clientId, ct)
            ?? throw new InvalidOperationException(
                $"No ClientSite configuration found for client '{clientId}'. " +
                "Provision the client site via the Admin portal before calling document operations.");

        var key = site.StorageBackend switch
        {
            StorageBackend.SharePointOnline    => "SP",
            StorageBackend.SharePointEmbedded  => "SPE",
            _ => throw new InvalidOperationException(
                $"Unsupported StorageBackend '{site.StorageBackend}' for client '{clientId}'.")
        };

        return _services.GetRequiredKeyedService<IDocumentStorageAdapter>(key);
    }

    /// <summary>
    /// Returns the IPermissionService registered for the client's backend.
    /// </summary>
    public async Task<IPermissionService> ResolvePermissionServiceAsync(string clientId, CancellationToken ct)
    {
        var site = await _siteRepo.GetByClientIdAsync(clientId, ct)
            ?? throw new InvalidOperationException($"No ClientSite config for '{clientId}'.");

        var key = site.StorageBackend switch
        {
            StorageBackend.SharePointOnline   => "SP",
            StorageBackend.SharePointEmbedded => "SPE",
            _ => throw new InvalidOperationException($"Unknown backend for '{clientId}'.")
        };

        return _services.GetRequiredKeyedService<IPermissionService>(key);
    }
}
