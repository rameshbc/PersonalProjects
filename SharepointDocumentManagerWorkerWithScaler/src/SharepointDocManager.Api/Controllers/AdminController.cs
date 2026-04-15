using Microsoft.AspNetCore.Mvc;
using SharepointDocManager.Application.Commands;
using SharepointDocManager.Application.Handlers;
using SharepointDocManager.Core.Entities;
using SharepointDocManager.Core.Enums;
using SharepointDocManager.Core.Interfaces;
using SharepointDocManager.Core.Models;

namespace SharepointDocManager.Api.Controllers;

/// <summary>
/// Admin-only endpoints for site provisioning and client management.
/// In production, protect with an admin role claim check (e.g. Azure AD app role).
/// </summary>
[ApiController]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly ProvisionClientSiteHandler _provisionHandler;
    private readonly IClientSiteRepository      _siteRepo;
    private readonly ILogger<AdminController>   _logger;

    public AdminController(
        ProvisionClientSiteHandler provisionHandler,
        IClientSiteRepository siteRepo,
        ILogger<AdminController> logger)
    {
        _provisionHandler = provisionHandler;
        _siteRepo         = siteRepo;
        _logger           = logger;
    }

    /// <summary>GET api/admin/clients — lists all configured clients.</summary>
    [HttpGet("clients")]
    public async Task<IActionResult> GetClients(CancellationToken ct)
    {
        var clients = await _siteRepo.GetAllAsync(ct);
        return Ok(clients);
    }

    /// <summary>
    /// POST api/admin/clients — provisions a new client site + folder tree.
    /// </summary>
    [HttpPost("clients")]
    public async Task<IActionResult> ProvisionClient(
        [FromBody] ProvisionClientRequest request, CancellationToken ct)
    {
        await _provisionHandler.HandleAsync(new ProvisionClientSiteCommand(
            request.ClientId,
            request.TenantId,
            request.StorageBackend,
            request.SpSiteUrl,
            request.SpeContainerId,
            request.FolderSpec), ct);

        return CreatedAtAction(nameof(GetClients), null);
    }

    /// <summary>
    /// PATCH api/admin/clients/{clientId}/storage-backend
    /// Switches a client between SP and SPE. Takes effect on next request.
    /// Content migration (SP → SPE) must be completed before switching.
    /// </summary>
    [HttpPatch("clients/{clientId}/storage-backend")]
    public async Task<IActionResult> SwitchStorageBackend(
        string clientId,
        [FromBody] StorageBackendSwitchRequest request,
        CancellationToken ct)
    {
        var site = await _siteRepo.GetByClientIdAsync(clientId, ct);
        if (site is null) return NotFound();

        site.StorageBackend = request.StorageBackend;
        site.UpdatedAt      = DateTimeOffset.UtcNow;
        await _siteRepo.UpsertAsync(site, ct);

        _logger.LogInformation(
            "[Admin] Switched client '{Client}' storage backend to {Backend}.",
            clientId, request.StorageBackend);

        return NoContent();
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public sealed record ProvisionClientRequest(
    string              ClientId,
    string              TenantId,
    StorageBackend      StorageBackend,
    string              SpSiteUrl,
    string              SpeContainerId,
    FolderStructureSpec FolderSpec);

public sealed record StorageBackendSwitchRequest(StorageBackend StorageBackend);
