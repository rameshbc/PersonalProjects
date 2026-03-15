using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OfficeScriptWorkflow.Worker.Configuration;
using OfficeScriptWorkflow.Worker.Services;

namespace OfficeScriptWorkflow.Worker.Tests;

public class WorkbookRegistryTests
{
    private static WorkbookRegistry Build(params WorkbookInstanceConfig[] workbooks)
    {
        var options = Options.Create(new WorkbookRegistryOptions { Workbooks = [.. workbooks] });
        return new WorkbookRegistry(options, NullLogger<WorkbookRegistry>.Instance);
    }

    private static WorkbookInstanceConfig MakeConfig(string id, string displayName = "Test") =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            StorageType = WorkbookStorageType.SharePoint,
            SiteUrl = "https://tenant.sharepoint.com/sites/Finance",
            WorkbookPath = "/Shared Documents/Test.xlsx"
        };

    private static WorkbookInstanceConfig MakeSpeConfig(string id, string containerId = "b!container123") =>
        new()
        {
            Id = id,
            DisplayName = "SPE Test",
            StorageType = WorkbookStorageType.SharePointEmbedded,
            ContainerId = containerId,
            SiteUrl = $"https://tenant.sharepoint.com/contentstorage/CSP_{containerId}",
            WorkbookPath = "/Documents/Test.xlsx"
        };

    [Fact]
    public void Get_KnownId_ReturnsConfig()
    {
        var registry = Build(MakeConfig("wb-01"));

        var cfg = registry.Get("wb-01");

        Assert.Equal("wb-01", cfg.Id);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var registry = Build(MakeConfig("wb-01"));

        var cfg = registry.Get("WB-01");

        Assert.Equal("wb-01", cfg.Id);
    }

    [Fact]
    public void Get_UnknownId_ThrowsKeyNotFoundException()
    {
        var registry = Build(MakeConfig("wb-01"));

        var ex = Assert.Throws<KeyNotFoundException>(() => registry.Get("does-not-exist"));
        Assert.Contains("wb-01", ex.Message);  // registered IDs listed in message
    }

    [Fact]
    public void Exists_KnownId_ReturnsTrue()
    {
        var registry = Build(MakeConfig("wb-01"));

        Assert.True(registry.Exists("wb-01"));
    }

    [Fact]
    public void Exists_UnknownId_ReturnsFalse()
    {
        var registry = Build(MakeConfig("wb-01"));

        Assert.False(registry.Exists("wb-99"));
    }

    [Fact]
    public void WorkbookIds_ContainsAllIds()
    {
        var registry = Build(MakeConfig("wb-01"), MakeConfig("wb-02"), MakeConfig("wb-03"));

        Assert.Equal(3, registry.WorkbookIds.Count);
        Assert.Contains("wb-01", registry.WorkbookIds);
        Assert.Contains("wb-02", registry.WorkbookIds);
        Assert.Contains("wb-03", registry.WorkbookIds);
    }

    [Fact]
    public void MultipleWorkbooks_EachReturnsCorrectConfig()
    {
        var registry = Build(MakeConfig("wb-01", "First"), MakeConfig("wb-02", "Second"));

        Assert.Equal("First", registry.Get("wb-01").DisplayName);
        Assert.Equal("Second", registry.Get("wb-02").DisplayName);
    }

    // ── StorageType — SharePoint (default) ────────────────────────────────────

    [Fact]
    public void SharePointWorkbook_DefaultStorageType_IsSharePoint()
    {
        var registry = Build(MakeConfig("wb-sp"));

        Assert.Equal(WorkbookStorageType.SharePoint, registry.Get("wb-sp").StorageType);
    }

    [Fact]
    public void SharePointWorkbook_SiteUrlAndPath_Preserved()
    {
        var cfg = MakeConfig("wb-sp");
        var registry = Build(cfg);

        var result = registry.Get("wb-sp");
        Assert.Equal("https://tenant.sharepoint.com/sites/Finance", result.SiteUrl);
        Assert.Equal("/Shared Documents/Test.xlsx", result.WorkbookPath);
    }

    // ── StorageType — SharePoint Embedded ─────────────────────────────────────

    [Fact]
    public void SpeWorkbook_StorageType_IsSharePointEmbedded()
    {
        var registry = Build(MakeSpeConfig("wb-spe"));

        Assert.Equal(WorkbookStorageType.SharePointEmbedded, registry.Get("wb-spe").StorageType);
    }

    [Fact]
    public void SpeWorkbook_ContainerId_Preserved()
    {
        var registry = Build(MakeSpeConfig("wb-spe", "b!container999"));

        Assert.Equal("b!container999", registry.Get("wb-spe").ContainerId);
    }

    [Fact]
    public void SpeWorkbook_MissingContainerId_ThrowsInvalidOperation()
    {
        var invalidSpe = new WorkbookInstanceConfig
        {
            Id = "wb-spe-bad",
            StorageType = WorkbookStorageType.SharePointEmbedded,
            ContainerId = "",   // missing — should fail validation
            SiteUrl = "https://tenant.sharepoint.com/contentstorage/CSP_xxx",
            WorkbookPath = "/Documents/Test.xlsx"
        };

        Assert.Throws<InvalidOperationException>(() => Build(invalidSpe));
    }

    // ── Mixed registry (SharePoint + SPE workbooks together) ─────────────────

    [Fact]
    public void MixedRegistry_SharePointAndSpe_BothResolvable()
    {
        var registry = Build(MakeConfig("wb-sp"), MakeSpeConfig("wb-spe"));

        Assert.Equal(WorkbookStorageType.SharePoint, registry.Get("wb-sp").StorageType);
        Assert.Equal(WorkbookStorageType.SharePointEmbedded, registry.Get("wb-spe").StorageType);
    }

    [Fact]
    public void MixedRegistry_DifferentSiteUrls_EachReturnedCorrectly()
    {
        var sp = new WorkbookInstanceConfig
        {
            Id = "wb-sp", StorageType = WorkbookStorageType.SharePoint,
            SiteUrl = "https://tenant.sharepoint.com/sites/Finance",
            WorkbookPath = "/Shared Documents/A.xlsx"
        };
        var spe = MakeSpeConfig("wb-spe", "b!container42");
        var registry = Build(sp, spe);

        Assert.Equal("https://tenant.sharepoint.com/sites/Finance", registry.Get("wb-sp").SiteUrl);
        Assert.StartsWith("https://tenant.sharepoint.com/contentstorage/CSP_", registry.Get("wb-spe").SiteUrl);
    }
}
