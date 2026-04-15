using SharepointDocManager.Core.Enums;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Application.Commands;

/// <summary>
/// Provisions a new client: creates ClientSite config, ensures role groups,
/// and provisions the DocLibrary-A folder tree with permissions.
/// Called by the Admin portal when onboarding a new client.
/// </summary>
public sealed record ProvisionClientSiteCommand(
    string              ClientId,
    string              TenantId,
    StorageBackend      StorageBackend,
    string              SpSiteUrl,      // SP: full site URL; SPE: leave empty
    string              SpeContainerId, // SPE: pre-created container ID; SP: leave empty
    FolderStructureSpec FolderSpec);
