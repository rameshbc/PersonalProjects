using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Polly.Registry;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Enums;
using SharepointDocManager.Core.Interfaces;

namespace SharepointDocManager.Infrastructure.Permissions;

/// <summary>
/// IPermissionService implementation for SharePoint Online.
///
/// Responsibilities:
///   1. Ensure the three Entra ID security groups exist per client
///      ({ClientId}-Admin, {ClientId}-Contributor, {ClientId}-Reader).
///   2. Break permission inheritance on a folder and grant each group its role.
///   3. Read current grants for permission drift detection.
///   4. Remove all grants and restore inheritance (offboarding).
///
/// Group naming convention: {clientId}-{Role}
///   e.g. "client-001-Admin", "client-001-Contributor", "client-001-Reader"
///
/// Graph permissions required on the MI app:
///   Group.ReadWrite.All — to create and query Entra security groups.
///   Sites.Selected      — to read/write site-level permissions.
/// </summary>
public sealed class SharePointPermissionService : IPermissionService
{
    private readonly GraphServiceClient               _graph;
    private readonly ResiliencePipelineProvider<string> _pipelines;
    private readonly IClientSiteRepository             _siteRepo;
    private readonly ILogger<SharePointPermissionService> _logger;

    public SharePointPermissionService(
        GraphServiceClient graph,
        ResiliencePipelineProvider<string> pipelines,
        IClientSiteRepository siteRepo,
        ILogger<SharePointPermissionService> logger)
    {
        _graph     = graph;
        _pipelines = pipelines;
        _siteRepo  = siteRepo;
        _logger    = logger;
    }

    // ── Group provisioning ────────────────────────────────────────────────────

    public async Task EnsureRoleGroupsAsync(string clientId, CancellationToken ct)
    {
        var roles = Enum.GetValues<DocumentRole>();
        var pipeline = _pipelines.GetPipeline("Standard");

        foreach (var role in roles)
        {
            var groupName = GroupName(clientId, role);

            // Check if group already exists
            var existing = await pipeline.ExecuteAsync(async token =>
                await _graph.Groups.GetAsync(req =>
                {
                    req.QueryParameters.Filter = $"displayName eq '{groupName}'";
                    req.QueryParameters.Select = ["id", "displayName"];
                }, token), ct);

            if (existing?.Value?.Count > 0)
            {
                _logger.LogDebug("[Permissions] Group '{Name}' already exists — skipped.", groupName);
                continue;
            }

            // Create the group
            var newGroup = await pipeline.ExecuteAsync(async token =>
                await _graph.Groups.PostAsync(new Group
                {
                    DisplayName     = groupName,
                    MailNickname    = groupName.Replace(" ", "-").ToLowerInvariant(),
                    SecurityEnabled = true,
                    MailEnabled     = false,
                    GroupTypes      = []
                }, cancellationToken: token), ct);

            _logger.LogInformation(
                "[Permissions] Created Entra group '{Name}' → {Id} for client {Client}",
                groupName, newGroup!.Id, clientId);
        }
    }

    // ── Folder permission assignment ──────────────────────────────────────────

    public async Task ApplyFolderPermissionsAsync(
        string clientId, string folderId,
        IEnumerable<PermissionGroup> groups, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        // Step 1: Remove all current explicit (non-inherited) grants to break inheritance
        var existing = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[folderId].Permissions
                .GetAsync(cancellationToken: token), ct);

        foreach (var perm in existing?.Value?.Where(p => p.InheritedFrom is null) ?? [])
        {
            await pipeline.ExecuteAsync(async token =>
            {
                await _graph.Drives[driveId].Items[folderId]
                    .Permissions[perm.Id].DeleteAsync(cancellationToken: token);
                return (object?)null;
            }, ct);
        }

        // Step 2: Grant each group
        foreach (var group in groups)
        {
            var role = MapRole(group.Role);

            await pipeline.ExecuteAsync(async token =>
            {
                await _graph.Drives[driveId].Items[folderId]
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
                "[Permissions] Granted '{Role}' → group '{Group}' on folder {Folder} (client {Client})",
                role, group.DisplayName, folderId, clientId);
        }
    }

    // ── Permission read ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PermissionGroup>> GetFolderPermissionsAsync(
        string clientId, string folderId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var perms = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[folderId].Permissions
                .GetAsync(cancellationToken: token), ct);

        return (perms?.Value ?? [])
            .Where(p => p.GrantedToIdentitiesV2 is not null)
            .SelectMany(p => p.GrantedToIdentitiesV2!.Select(identity => new PermissionGroup
            {
                GroupId      = identity.Group?.Id ?? string.Empty,
                DisplayName  = identity.Group?.DisplayName ?? string.Empty,
                Role         = MapRoleReverse(p.Roles?.FirstOrDefault()),
                PermissionId = p.Id
            }))
            .ToList();
    }

    // ── Offboarding ───────────────────────────────────────────────────────────

    public async Task RemoveFolderPermissionsAsync(string clientId, string folderId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var perms = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[folderId].Permissions
                .GetAsync(cancellationToken: token), ct);

        foreach (var perm in perms?.Value?.Where(p => p.InheritedFrom is null) ?? [])
        {
            await pipeline.ExecuteAsync(async token =>
            {
                await _graph.Drives[driveId].Items[folderId]
                    .Permissions[perm.Id].DeleteAsync(cancellationToken: token);
                return (object?)null;
            }, ct);
        }

        _logger.LogInformation(
            "[Permissions] Removed all explicit grants from folder {Folder} (client {Client})",
            folderId, clientId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> GetDriveIdAsync(string clientId, CancellationToken ct)
    {
        var site = await _siteRepo.GetByClientIdAsync(clientId, ct)
            ?? throw new InvalidOperationException($"No ClientSite found for '{clientId}'.");
        return site.SpDriveId;
    }

    private static string GroupName(string clientId, DocumentRole role) => $"{clientId}-{role}";

    private static string MapRole(DocumentRole role) => role switch
    {
        DocumentRole.Admin       => "owner",
        DocumentRole.Contributor => "write",
        DocumentRole.Reader      => "read",
        _                        => "read"
    };

    private static DocumentRole MapRoleReverse(string? role) => role switch
    {
        "owner" => DocumentRole.Admin,
        "write" => DocumentRole.Contributor,
        _       => DocumentRole.Reader
    };
}
