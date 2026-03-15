using System.ComponentModel.DataAnnotations;

namespace OfficeScriptWorkflow.Worker.Configuration;

/// <summary>
/// Top-level registry of all workbook instances the worker service manages.
/// In a multi-replica deployment each replica reads the same registry and
/// handles whichever workbook is assigned to it via Azure Service Bus sessions.
/// </summary>
public class WorkbookRegistryOptions
{
    [Required, MinLength(1, ErrorMessage = "At least one workbook must be configured.")]
    public List<WorkbookInstanceConfig> Workbooks { get; set; } = [];
}
