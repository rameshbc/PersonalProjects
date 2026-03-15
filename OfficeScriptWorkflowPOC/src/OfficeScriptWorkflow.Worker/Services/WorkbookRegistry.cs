using Microsoft.Extensions.Options;
using OfficeScriptWorkflow.Worker.Configuration;

namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Singleton registry loaded from <see cref="WorkbookRegistryOptions"/> at startup.
/// All lookups are O(1) dictionary reads — safe for high-frequency concurrent access.
/// </summary>
public sealed class WorkbookRegistry : IWorkbookRegistry
{
    private readonly IReadOnlyDictionary<string, WorkbookInstanceConfig> _index;
    private readonly ILogger<WorkbookRegistry> _logger;

    public IReadOnlyList<string> WorkbookIds { get; }

    public WorkbookRegistry(
        IOptions<WorkbookRegistryOptions> options,
        ILogger<WorkbookRegistry> logger)
    {
        _logger = logger;

        var workbooks = options.Value.Workbooks;

        ValidateStorageConfig(workbooks);

        _index = workbooks.ToDictionary(
            w => w.Id,
            w => w,
            StringComparer.OrdinalIgnoreCase);

        WorkbookIds = [.. _index.Keys];

        _logger.LogInformation(
            "WorkbookRegistry initialised with {Count} workbook(s): [{Ids}]",
            workbooks.Count,
            string.Join(", ", WorkbookIds));

        foreach (var w in workbooks)
        {
            _logger.LogInformation(
                "  Workbook '{Id}' ({DisplayName}) — StorageType={StorageType} SiteUrl={SiteUrl}",
                w.Id, w.DisplayName, w.StorageType, w.SiteUrl);
        }
    }

    private static void ValidateStorageConfig(List<WorkbookInstanceConfig> workbooks)
    {
        var errors = new List<string>();

        foreach (var w in workbooks)
        {
            if (w.StorageType == WorkbookStorageType.SharePointEmbedded
                && string.IsNullOrWhiteSpace(w.ContainerId))
            {
                errors.Add(
                    $"Workbook '{w.Id}': ContainerId is required when StorageType = SharePointEmbedded.");
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "WorkbookRegistry configuration errors:\n" + string.Join("\n", errors));
    }

    public WorkbookInstanceConfig Get(string workbookId)
    {
        if (_index.TryGetValue(workbookId, out var config))
            return config;

        throw new KeyNotFoundException(
            $"Workbook '{workbookId}' is not in the registry. " +
            $"Registered IDs: [{string.Join(", ", WorkbookIds)}]");
    }

    public bool Exists(string workbookId) =>
        _index.ContainsKey(workbookId);
}
