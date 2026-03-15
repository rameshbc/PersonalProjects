using OfficeScriptWorkflow.Worker.Configuration;

namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Resolves workbook configuration by ID.
/// In a multi-replica deployment each replica shares the same registry (loaded from config).
/// The registry is immutable at runtime — workbook config changes require a restart.
/// </summary>
public interface IWorkbookRegistry
{
    /// <summary>
    /// Returns the configuration for the given workbook ID.
    /// Throws <see cref="KeyNotFoundException"/> if the ID is not registered.
    /// </summary>
    WorkbookInstanceConfig Get(string workbookId);

    /// <summary>Returns all registered workbook IDs.</summary>
    IReadOnlyList<string> WorkbookIds { get; }

    /// <summary>Returns true if a workbook with this ID exists in the registry.</summary>
    bool Exists(string workbookId);
}
