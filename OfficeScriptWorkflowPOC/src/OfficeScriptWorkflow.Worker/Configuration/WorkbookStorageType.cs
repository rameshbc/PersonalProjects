namespace OfficeScriptWorkflow.Worker.Configuration;

/// <summary>
/// Identifies where a workbook is stored.
///
/// SharePoint (default):
///   Workbook lives in a SharePoint document library on a standard SharePoint site.
///   SiteUrl = https://TENANT.sharepoint.com/sites/SITENAME
///   WorkbookPath = /Shared Documents/Workbooks/MyWorkbook.xlsx
///
/// SharePointEmbedded:
///   Workbook lives inside a SharePoint Embedded (SPE) container — an isolated,
///   app-owned file storage container provisioned via Microsoft Graph.
///   Each container has its own site URL derived from its ContainerId:
///   SiteUrl = https://TENANT.sharepoint.com/contentstorage/CSP_CONTAINERID
///   WorkbookPath = /Documents/MyWorkbook.xlsx
///   ContainerId must also be set for permission management via Graph API.
/// </summary>
public enum WorkbookStorageType
{
    /// <summary>Standard SharePoint document library (default).</summary>
    SharePoint,

    /// <summary>SharePoint Embedded app-owned container.</summary>
    SharePointEmbedded
}
