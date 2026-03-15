using System.ComponentModel.DataAnnotations;

namespace OfficeScriptWorkflow.Worker.Configuration;

/// <summary>
/// Configuration for a single workbook instance — its storage location and
/// the Power Automate flow URLs that operate on it.
///
/// Each workbook has its own set of flows because the "Run script" action in
/// Power Automate is hardcoded to a specific file at design time.
///
/// Two storage backends are supported:
///   - SharePoint (default): standard SharePoint document library on any site.
///   - SharePointEmbedded: app-owned SPE container accessed via its container URL.
/// </summary>
public class WorkbookInstanceConfig
{
    /// <summary>
    /// Unique identifier used when routing operations. Must be URL-safe (no spaces).
    /// Example: "financials", "operations-eu", "risk-model"
    /// </summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable label used in log messages.</summary>
    public string DisplayName { get; set; } = string.Empty;

    // ── Storage backend ──────────────────────────────────────────────────────

    /// <summary>
    /// Where the workbook is stored. Defaults to SharePoint.
    /// Controls how SiteUrl and ContainerId are interpreted and validated.
    /// </summary>
    public WorkbookStorageType StorageType { get; set; } = WorkbookStorageType.SharePoint;

    // ── SharePoint properties (required for StorageType = SharePoint) ────────

    /// <summary>
    /// Full URL of the SharePoint site that hosts this workbook.
    ///
    /// SharePoint:          https://TENANT.sharepoint.com/sites/SITENAME
    /// SharePointEmbedded:  https://TENANT.sharepoint.com/contentstorage/CSP_CONTAINERID
    ///
    /// Each workbook can be on a different site or SPE container.
    /// </summary>
    [Required]
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>
    /// Server-relative path to the workbook file within the site/container.
    ///
    /// SharePoint:          /Shared Documents/Workbooks/Financials.xlsx
    /// SharePointEmbedded:  /Documents/Financials.xlsx
    /// </summary>
    [Required]
    public string WorkbookPath { get; set; } = string.Empty;

    // ── SharePoint Embedded properties (required when StorageType = SharePointEmbedded) ─

    /// <summary>
    /// SharePoint Embedded container ID. Required when StorageType = SharePointEmbedded.
    ///
    /// Format: b!{base64-encoded-site-id}_{base64-encoded-web-id}
    /// Retrieve via Graph API: GET /storage/fileStorage/containers
    ///
    /// Used for:
    ///   - Container permission management (Graph API)
    ///   - Deriving the container SiteUrl:
    ///     https://TENANT.sharepoint.com/contentstorage/CSP_{ContainerId}
    ///   - Logging and diagnostics
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// SPE container type ID, issued when registering the container type with Microsoft.
    /// Used when provisioning containers via Graph API.
    /// Not required for day-to-day operation once flows are configured.
    /// </summary>
    public string ContainerTypeId { get; set; } = string.Empty;

    // ── Batching ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum rows sent in a single Office Script invocation.
    /// Office Scripts time out at 5 minutes — keep batches well below that threshold.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    // ── Power Automate flow URLs (SAS-signed — treat as secrets) ─────────────

    [Required]
    public string InsertRangeFlowUrl { get; set; } = string.Empty;

    [Required]
    public string UpdateRangeFlowUrl { get; set; } = string.Empty;

    [Required]
    public string ExtractRangeFlowUrl { get; set; } = string.Empty;

    /// <summary>
    /// URL for the BatchOperations Power Automate flow.
    /// Required when using IExcelWorkbookService.ExecuteBatchAsync().
    /// Leave empty if only using individual insert/update/extract flows.
    /// </summary>
    public string BatchOperationFlowUrl { get; set; } = string.Empty;
}
