using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Polly.Registry;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Enums;
using SharepointDocManager.Core.Interfaces;

namespace SharepointDocManager.Infrastructure.Permissions;

/// <summary>
/// IPermissionService implementation for SharePoint Embedded (SPE).
///
/// SPE permission model differs from SP in one key way:
///   • Container-level roles (Owner / Manager / Reader / Writer) control coarse access.
///     These are set on the container itself via:
///       POST /storage/fileStorage/containers/{containerId}/permissions
///   • Drive item-level /invite grants (same as SP) control fine-grained folder access.
///
/// This service manages both levels:
///   EnsureRoleGroupsAsync    → Entra group creation (identical to SP)
///   ApplyFolderPermissionsAsync → drive item /invite (identical to SP)
///   EnsureContainerPermissionsAsync → container-level grant (SPE-specific)
///
/// Graph permissions required:
///   Group.ReadWrite.All           — Entra group management
///   FileStorageContainer.Selected — container permission management
/// </summary>
public sealed class SpePermissionService : IPermissionService
{
    private readonly GraphServiceClient               _graph;
    private readonly ResiliencePipelineProvider<string> _pipelines;
    private readonly IClientSiteRepository             _siteRepo;
    private readonly ILogger<SpePermissionService>    _logger;

    public SpePermissionService(
        GraphServiceClient graph,
        ResiliencePipelineProvider<string> pipelines,
        IClientSiteRepository siteRepo,
        ILogger<SpePermissionService> logger)
    {
        _graph     = graph;
        _pipelines = pipelines;
        _siteRepo  = siteRepo;
        _logger    = logger;
    }

    // ── Group provisioning — identical to SP implementation ───────────────────

    public async Task EnsureRoleGroupsAsync(string clientId, CancellationToken ct)
    {
        var pipeline = _pipelines.GetPipeline("Standard");

        foreach (var role in Enum.GetValues<DocumentRole>())
        {
            var groupName = $"{clientId}-{role}";

            var existing = await pipeline.ExecuteAsync(async token =>
                await _graph.Groups.GetAsync(req =>
                {
                    req.QueryParameters.Filter = $"displayName eq '{groupName}'";
                    req.QueryParameters.Select = ["id", "displayName"];
                }, token), ct);

            if (existing?.Value?.Count > 0)
            {
                _logger.LogDebug("[SPE Permissions] Group '{Name}' exists.", groupName);
                continue;
            }

            await pipeline.ExecuteAsync(async token =>
                await _graph.Groups.PostAsync(new Group
                {
                    DisplayName     = groupName,
                    MailNickname    = groupName.Replace(" ", "-").ToLowerInvariant(),
                    SecurityEnabled = true,
                    MailEnabled     = false,
                    GroupTypes      = []
                }, cancellationToken: token), ct);

            _logger.LogInformation("[SPE Permissions] Created group '{Name}' for client {Client}.", groupName, clientId);
        }
    }

    // ── Folder-level permissions (drive item invite) ───────────────────────────

    public async Task ApplyFolderPermissionsAsync(
        string clientId, string folderId,
        IEnumerable<PermissionGroup> groups, CancellationToken ct)
    {
        var containerId = await GetContainerIdAsync(clientId, ct);
        var pipeline    = _pipelines.GetPipeline("Standard");

        // Remove existing explicit grants on this folder
        var existing = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[containerId].Items[folderId].Permissions
                .GetAsync(cancellationToken: token), ct);

        foreach (var perm in existing?.Value?.Where(p => p.InheritedFrom is null) ?? [])
        {
            await pipeline.ExecuteAsync(async token =>
            {
                await _graph.Drives[containerId].Items[folderId]
                    .Permissions[perm.Id].DeleteAsync(cancellationToken: token);
                return (object?)null;
            }, ct);
        }

        // Grant each group
        foreach (var group in groups)
        {
            var role = group.Role switch
            {
                DocumentRole.Admin       => "owner",
                DocumentRole.Contributor => "write",
                DocumentRole.Reader      => "read",
                _                        => "read"
            };

            await pipeline.ExecuteAsync(async token =>
            {
                await _graph.Drives[containerId].Items[folderId]
                    .Invite.PostAsInvitePostResponseAsync(
                        new Microsoft.Graph.Drives.Item.Items.Item.Invite.InvitePostRequestBody
                        {
                            Roles          = [role],
                            SendInvitation = false,
                            RequireSignIn  = true,
                            Recipients     = [new DriveRecipient { ObjectId = group.GroupId }]
                        }, cancellationToken: token);
                return (object?)null;
            }, ct);

            _logger.LogInformation(
                "[SPE Permissions] Granted '{Role}' → group '{Group}' on folder {Folder} (client {Client})",
                role, group.DisplayName, folderId, clientId);
        }
    }

    public async Task<IReadOnlyList<PermissionGroup>> GetFolderPermissionsAsync(
        string clientId, string folderId, CancellationToken ct)
    {
        var containerId = await GetContainerIdAsync(clientId, ct);
        var pipeline    = _pipelines.GetPipeline("Standard");

        var perms = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[containerId].Items[folderId].Permissions
                .GetAsync(cancellationToken: token), ct);

        return (perms?.Value ?? [])
            .Where(p => p.GrantedToIdentitiesV2 is not null)
            .SelectMany(p => p.GrantedToIdentitiesV2!.Select(identity => new PermissionGroup
            {
                GroupId      = identity.Group?.Id ?? string.Empty,
                DisplayName  = identity.Group?.DisplayName ?? string.Empty,
                Role         = p.Roles?.FirstOrDefault() switch
                {
                    "owner" => DocumentRole.Admin,
                    "write" => DocumentRole.Contributor,
                    _       => DocumentRole.Reader
                },
                PermissionId = p.Id
            }))
            .ToList();
    }

    public async Task RemoveFolderPermissionsAsync(string clientId, string folderId, CancellationToken ct)
    {
        var containerId = await GetContainerIdAsync(clientId, ct);
        var pipeline    = _pipelines.GetPipeline("Standard");

        var perms = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[containerId].Items[folderId].Permissions
                .GetAsync(cancellationToken: token), ct);

        foreach (var perm in perms?.Value?.Where(p => p.InheritedFrom is null) ?? [])
        {
            await pipeline.ExecuteAsync(async token =>
            {
                await _graph.Drives[containerId].Items[folderId]
                    .Permissions[perm.Id].DeleteAsync(cancellationToken: token);
                return (object?)null;
            }, ct);
        }

        _logger.LogInformation(
            "[SPE Permissions] Cleared folder grants on {Folder} (client {Client})", folderId, clientId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> GetContainerIdAsync(string clientId, CancellationToken ct)
    {
        var site = await _siteRepo.GetByClientIdAsync(clientId, ct)
            ?? throw new InvalidOperationException($"No ClientSite found for '{clientId}'.");
        return site.SpeContainerId;
    }
}
