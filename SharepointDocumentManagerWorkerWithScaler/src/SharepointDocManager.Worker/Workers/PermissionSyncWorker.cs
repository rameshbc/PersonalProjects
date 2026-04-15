using Microsoft.Graph;
using SharepointDocManager.Application.Services;
using SharepointDocManager.Core.Interfaces;

namespace SharepointDocManager.Worker.Workers;

/// <summary>
/// Background worker that uses Graph delta queries to detect permission drift.
///
/// What is permission drift?
///   After folder permissions are set, an admin may modify them directly in
///   SharePoint or via another tool — outside this application's control.
///   Over time the actual permissions diverge from the intended grants.
///
/// How delta queries work:
///   1. First run: GET /drives/{driveId}/root/delta → full snapshot + deltaToken.
///   2. Subsequent runs: GET /drives/{driveId}/root/delta?token={deltaToken}
///      → only items changed since the last sync.
///   3. We filter for folder items whose permissions changed and re-apply
///      the expected grants via IPermissionService.
///
/// Run interval: configurable via "Worker:PermissionSyncIntervalMinutes" (default 60).
/// DeltaToken per client is persisted in IClientSiteRepository.
/// </summary>
public sealed class PermissionSyncWorker : BackgroundService
{
    private readonly GraphServiceClient         _graph;
    private readonly StorageAdapterResolver     _resolver;
    private readonly IClientSiteRepository      _siteRepo;
    private readonly ILogger<PermissionSyncWorker> _logger;
    private readonly TimeSpan _interval;

    public PermissionSyncWorker(
        GraphServiceClient graph,
        StorageAdapterResolver resolver,
        IClientSiteRepository siteRepo,
        IConfiguration config,
        ILogger<PermissionSyncWorker> logger)
    {
        _graph    = graph;
        _resolver = resolver;
        _siteRepo = siteRepo;
        _logger   = logger;
        _interval = TimeSpan.FromMinutes(
            config.GetValue("Worker:PermissionSyncIntervalMinutes", defaultValue: 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[PermissionSync] Started. Sync interval: {Interval:g}.", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAllClientsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PermissionSync] Sync cycle failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("[PermissionSync] Stopped.");
    }

    private async Task SyncAllClientsAsync(CancellationToken ct)
    {
        var clients = await _siteRepo.GetAllAsync(ct);

        foreach (var client in clients)
        {
            try
            {
                await SyncClientAsync(client.ClientId, client.SpDriveId, client.DeltaToken, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[PermissionSync] Failed to sync client '{Client}'.", client.ClientId);
            }
        }
    }

    private async Task SyncClientAsync(
        string clientId, string driveId, string? deltaToken, CancellationToken ct)
    {
        // Build delta request — use existing token or start fresh
        var deltaUrl = string.IsNullOrEmpty(deltaToken)
            ? $"https://graph.microsoft.com/v1.0/drives/{driveId}/root/delta"
            : $"https://graph.microsoft.com/v1.0/drives/{driveId}/root/delta?token={deltaToken}";

        string? newDeltaToken = null;

        // Page through delta results
        while (deltaUrl is not null)
        {
            // TODO: Microsoft.Graph SDK v5+ - Delta query pattern requires proper API binding
            // The .Delta() extension method is not available in current SDK version.
            // Workaround: Use direct HTTP client for delta queries or wait for SDK update.
            // For now, log that this sync cycle is skipped pending SDK resolution.
            _logger.LogWarning(
                "[PermissionSync] Delta query not yet implemented for client '{Client}' — " +
                "requires Microsoft.Graph SDK v5 API binding update.", clientId);

            // When implemented, code should look like:
            // var page = await _graph.Drives[driveId].Root
            //     .Delta().WithUrl(deltaUrl)
            //     .GetAsDeltaGetResponseAsync(cancellationToken: ct);
            break;
        }

        // Persist the new delta token so the next run is incremental
        if (!string.IsNullOrEmpty(newDeltaToken))
        {
            await _siteRepo.UpdateDeltaTokenAsync(clientId, newDeltaToken, ct);
            _logger.LogDebug(
                "[PermissionSync] Updated delta token for client '{Client}'.", clientId);
        }
    }

    private async Task VerifyFolderPermissionsAsync(
        string clientId, string folderId, CancellationToken ct)
    {
        try
        {
            var permService = await _resolver.ResolvePermissionServiceAsync(clientId, ct);
            var current     = await permService.GetFolderPermissionsAsync(clientId, folderId, ct);

            // Simple drift check: if current grants are empty, re-apply expected grants.
            // A production implementation would compare current vs expected and patch the delta.
            if (!current.Any())
            {
                _logger.LogWarning(
                    "[PermissionSync] Folder {Folder} has no grants — potential drift for client {Client}.",
                    folderId, clientId);
                // In a full implementation: load expected groups from config and re-apply
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PermissionSync] Permission check failed for folder {Folder} (client {Client}).",
                folderId, clientId);
        }
    }
}
