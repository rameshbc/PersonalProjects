using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Polly.Registry;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Interfaces;
using SharepointDocManager.Core.Models;
using SharepointDocManager.Infrastructure.Graph;
using SharepointDocManager.Infrastructure.Resilience;

namespace SharepointDocManager.Infrastructure.Adapters;

/// <summary>
/// IDocumentStorageAdapter implementation for SharePoint Online.
///
/// All Graph calls go through:
///   BulkheadPolicy      → limits concurrent calls per client
///   ResiliencePipeline  → retry + circuit breaker
///   GraphThrottlingHandler → 429 / Retry-After (in the HttpClient pipeline)
///
/// Drive resolution:
///   The ClientSite entity holds the resolved SpDriveId. This adapter
///   retrieves it via IClientSiteRepository — no per-call site/drive lookups.
/// </summary>
public sealed class SharePointAdapter : IDocumentStorageAdapter
{
    private const long SmallFileThreshold = 4 * 1024 * 1024; // 4 MB

    private readonly GraphServiceClient           _graph;
    private readonly GraphUploadSessionManager    _uploadManager;
    private readonly BulkheadPolicy              _bulkhead;
    private readonly ResiliencePipelineProvider<string> _pipelines;
    private readonly IClientSiteRepository        _siteRepo;
    private readonly ILogger<SharePointAdapter>   _logger;

    public SharePointAdapter(
        GraphServiceClient graph,
        GraphUploadSessionManager uploadManager,
        BulkheadPolicy bulkhead,
        ResiliencePipelineProvider<string> pipelines,
        IClientSiteRepository siteRepo,
        ILogger<SharePointAdapter> logger)
    {
        _graph         = graph;
        _uploadManager = uploadManager;
        _bulkhead      = bulkhead;
        _pipelines     = pipelines;
        _siteRepo      = siteRepo;
        _logger        = logger;
    }

    // ── Folder operations ─────────────────────────────────────────────────────

    public async Task<string> CreateFolderAsync(
        string clientId, string parentItemId, string folderName, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        return await _bulkhead.ExecuteAsync(clientId, async token =>
        {
            return await pipeline.ExecuteAsync(async pipToken =>
            {
                try
                {
                    var newFolder = await _graph.Drives[driveId]
                        .Items[parentItemId].Children
                        .PostAsync(new DriveItem
                        {
                            Name   = folderName,
                            Folder = new Folder(),
                            AdditionalData = new Dictionary<string, object>
                            {
                                ["@microsoft.graph.conflictBehavior"] = "fail"
                            }
                        }, cancellationToken: pipToken);

                    _logger.LogInformation(
                        "[SP] Created folder '{Name}' → {Id} (client {Client})",
                        folderName, newFolder!.Id, clientId);

                    return newFolder.Id!;
                }
                catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
                    when (ex.ResponseStatusCode == 409)
                {
                    // Folder already exists — fetch its ID (idempotent)
                    _logger.LogDebug("[SP] Folder '{Name}' already exists — fetching ID.", folderName);
                    return await GetExistingFolderIdAsync(driveId, parentItemId, folderName, pipToken);
                }
            }, token);
        }, ct);
    }

    public async Task<IReadOnlyList<DocumentFolder>> ListFoldersAsync(
        string clientId, string parentItemId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var items = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[parentItemId].Children
                .GetAsync(req =>
                {
                    req.QueryParameters.Filter = "folder ne null";
                    req.QueryParameters.Select = ["id", "name", "folder", "parentReference"];
                }, token), ct);

        return (items?.Value ?? [])
            .Select(i => new DocumentFolder
            {
                DriveItemId  = i.Id!,
                Name         = i.Name!,
                ParentItemId = i.ParentReference?.Id
            })
            .ToList();
    }

    // ── Document upload ───────────────────────────────────────────────────────

    public async Task<DocumentItem> UploadDocumentAsync(UploadRequest request, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(request.ClientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        return await _bulkhead.ExecuteAsync(request.ClientId, async token =>
        {
            DriveItem driveItem;

            if (request.ContentLength < SmallFileThreshold)
            {
                // Single PUT — simple and fast for small files
                driveItem = await pipeline.ExecuteAsync(async pipToken =>
                    await _graph.Drives[driveId]
                        .Items[request.ParentFolderId]
                        .ItemWithPath(request.FileName)
                        .Content
                        .PutAsync(request.Content, cancellationToken: pipToken), token);
            }
            else
            {
                // Resumable session — chunked upload for large files
                driveItem = await _uploadManager.UploadLargeFileAsync(
                    driveId, request.ParentFolderId, request.FileName,
                    request.Content,
                    request.ConflictBehaviour.ToString().ToLowerInvariant(),
                    token);
            }

            _logger.LogInformation(
                "[SP] Uploaded '{File}' → {Id} ({Size:N0} bytes) for client {Client}",
                request.FileName, driveItem.Id, request.ContentLength, request.ClientId);

            return MapToDomainItem(driveItem);
        }, ct);
    }

    public async Task<BatchOperationResult> BatchUploadAsync(
        IEnumerable<UploadRequest> requests, CancellationToken ct)
    {
        var requestList = requests.ToList();
        var results     = new List<ItemOperationResult>();

        // Upload concurrently, bounded by the client's bulkhead
        await Parallel.ForEachAsync(requestList,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (req, token) =>
            {
                try
                {
                    var item = await UploadDocumentAsync(req, token);
                    lock (results)
                        results.Add(new ItemOperationResult { ItemName = req.FileName, Success = true, Item = item });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SP] Batch upload failed for '{File}'.", req.FileName);
                    lock (results)
                        results.Add(new ItemOperationResult
                        {
                            ItemName     = req.FileName,
                            Success      = false,
                            ErrorMessage = ex.Message
                        });
                }
            });

        return new BatchOperationResult
        {
            TotalRequested = requestList.Count,
            Results        = results
        };
    }

    // ── Document listing ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DocumentItem>> ListDocumentsAsync(
        string clientId, string folderId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var page = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[folderId].Children
                .GetAsync(req =>
                {
                    req.QueryParameters.Filter = "file ne null";
                    req.QueryParameters.Select =
                    [
                        "id", "name", "size", "file", "webUrl",
                        "lastModifiedDateTime", "createdDateTime",
                        "lastModifiedBy", "createdBy", "eTag", "parentReference"
                    ];
                    req.QueryParameters.Top = 200;
                }, token), ct);

        return (page?.Value ?? []).Select(MapToDomainItem).ToList();
    }

    // ── Version history ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DocumentVersion>> GetVersionHistoryAsync(
        string clientId, string itemId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var versions = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[itemId].Versions
                .GetAsync(cancellationToken: token), ct);

        return (versions?.Value ?? [])
            .Select(v => new DocumentVersion
            {
                VersionId      = v.Id!,
                DriveItemId    = itemId,
                VersionLabel   = v.Id,
                SizeBytes      = v.Size ?? 0,
                LastModifiedBy = v.LastModifiedBy?.User?.DisplayName,
                LastModifiedAt = v.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                DownloadUrl    = v.AdditionalData?.TryGetValue("@microsoft.graph.downloadUrl", out var u) == true
                                     ? u?.ToString() : null
            })
            .OrderByDescending(v => v.LastModifiedAt)
            .ToList();
    }

    // ── Online edit URL ───────────────────────────────────────────────────────

    public async Task<string> GetOnlineEditUrlAsync(string clientId, string itemId, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        var item = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[itemId]
                .GetAsync(req => req.QueryParameters.Select = ["webUrl"], token), ct);

        // Appending ?web=1 forces Office Online to open in edit mode
        return item?.WebUrl is not null
            ? $"{item.WebUrl}?web=1"
            : throw new InvalidOperationException($"No webUrl for item {itemId}.");
    }

    // ── Permissions ───────────────────────────────────────────────────────────

    public async Task SetFolderPermissionsAsync(
        string clientId, string folderId,
        IEnumerable<PermissionGroup> groups, CancellationToken ct)
    {
        var driveId  = await GetDriveIdAsync(clientId, ct);
        var pipeline = _pipelines.GetPipeline("Standard");

        // Step 1: Break inheritance — remove all inherited permissions
        var existing = await pipeline.ExecuteAsync(async token =>
            await _graph.Drives[driveId].Items[folderId].Permissions
                .GetAsync(cancellationToken: token), ct);

        foreach (var perm in existing?.Value ?? [])
        {
            if (perm.InheritedFrom is not null) continue;   // Already inherited — skip
            await pipeline.ExecuteAsync(async token =>
            {
                await _graph.Drives[driveId].Items[folderId]
                    .Permissions[perm.Id].DeleteAsync(cancellationToken: token);
                return (object?)null;
            }, ct);
        }

        // Step 2: Grant each group its role
        foreach (var group in groups)
        {
            var role = group.Role switch
            {
                Core.Enums.DocumentRole.Admin       => "owner",
                Core.Enums.DocumentRole.Contributor => "write",
                Core.Enums.DocumentRole.Reader      => "read",
                _ => "read"
            };

            await pipeline.ExecuteAsync(async token =>
            {
                await _graph.Drives[driveId].Items[folderId]
                    .Invite.PostAsInvitePostResponseAsync(new Microsoft.Graph.Drives.Item.Items.Item.Invite.InvitePostRequestBody
                    {
                        Roles             = [role],
                        SendInvitation    = false,
                        RequireSignIn     = true,
                        Recipients        =
                        [
                            new Microsoft.Graph.Models.DriveRecipient
                            {
                                ObjectId = group.GroupId
                            }
                        ]
                    }, cancellationToken: token);
                return (object?)null;
            }, ct);

            _logger.LogInformation(
                "[SP] Granted '{Role}' to group '{Group}' on folder {Folder} (client {Client})",
                role, group.DisplayName, folderId, clientId);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string> GetDriveIdAsync(string clientId, CancellationToken ct)
    {
        var site = await _siteRepo.GetByClientIdAsync(clientId, ct)
            ?? throw new InvalidOperationException($"No ClientSite config found for '{clientId}'.");
        return site.SpDriveId;
    }

    private async Task<string> GetExistingFolderIdAsync(
        string driveId, string parentItemId, string folderName, CancellationToken ct)
    {
        var children = await _graph.Drives[driveId].Items[parentItemId].Children
            .GetAsync(req =>
            {
                req.QueryParameters.Filter = $"name eq '{folderName}' and folder ne null";
                req.QueryParameters.Select = ["id", "name"];
            }, ct);

        return children?.Value?.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException($"Folder '{folderName}' not found under parent '{parentItemId}'.");
    }

    private static DocumentItem MapToDomainItem(DriveItem i) => new()
    {
        DriveItemId    = i.Id!,
        Name           = i.Name!,
        SizeBytes      = i.Size ?? 0,
        MimeType       = i.File?.MimeType,
        ETag           = i.ETag,
        OnlineEditUrl  = i.WebUrl is not null ? $"{i.WebUrl}?web=1" : null,
        ParentFolderId = i.ParentReference?.Id ?? string.Empty,
        CreatedBy      = i.CreatedBy?.User?.DisplayName,
        LastModifiedBy = i.LastModifiedBy?.User?.DisplayName,
        CreatedAt      = i.CreatedDateTime ?? DateTimeOffset.UtcNow,
        LastModifiedAt = i.LastModifiedDateTime ?? DateTimeOffset.UtcNow
    };
}
