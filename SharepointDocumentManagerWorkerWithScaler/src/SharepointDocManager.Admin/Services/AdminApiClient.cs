using System.Net.Http.Json;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Enums;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Admin.Services;

/// <summary>
/// HTTP client for the SharepointDocManager.Api backend.
/// Used by Blazor Server pages to call admin and document endpoints.
///
/// Registered as a scoped service so each Blazor circuit gets its own instance.
/// BaseAddress is configured in appsettings.json → "Api:BaseUrl".
/// </summary>
public sealed class AdminApiClient
{
    private readonly HttpClient _http;

    public AdminApiClient(HttpClient http) => _http = http;

    // ── Client management ─────────────────────────────────────────────────────

    public Task<List<ClientSite>?> GetClientsAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<List<ClientSite>>("api/admin/clients", ct);

    public async Task ProvisionClientAsync(
        string clientId, string tenantId, StorageBackend backend,
        string spSiteUrl, string speContainerId, FolderStructureSpec folderSpec,
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/admin/clients", new
        {
            clientId, tenantId,
            storageBackend = backend,
            spSiteUrl, speContainerId, folderSpec
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SwitchStorageBackendAsync(
        string clientId, StorageBackend backend, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync(
            $"api/admin/clients/{clientId}/storage-backend",
            new { storageBackend = backend }, ct);
        response.EnsureSuccessStatusCode();
    }

    // ── Folder operations ─────────────────────────────────────────────────────

    public Task<List<DocumentFolder>?> GetFoldersAsync(
        string clientId, string parentFolderId, CancellationToken ct = default)
        => _http.GetFromJsonAsync<List<DocumentFolder>>(
            $"api/clients/{clientId}/folders/{parentFolderId}/children", ct);

    // ── Document operations ───────────────────────────────────────────────────

    public Task<List<DocumentItem>?> GetDocumentsAsync(
        string clientId, string folderId, CancellationToken ct = default)
        => _http.GetFromJsonAsync<List<DocumentItem>>(
            $"api/clients/{clientId}/folders/{folderId}/documents", ct);

    public Task<List<DocumentVersion>?> GetVersionHistoryAsync(
        string clientId, string itemId, CancellationToken ct = default)
        => _http.GetFromJsonAsync<List<DocumentVersion>>(
            $"api/clients/{clientId}/documents/{itemId}/versions", ct);

    public async Task<string?> GetEditUrlAsync(
        string clientId, string itemId, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<EditUrlResponse>(
            $"api/clients/{clientId}/documents/{itemId}/edit-url", ct);
        return result?.EditUrl;
    }

    private sealed record EditUrlResponse(string EditUrl);
}
