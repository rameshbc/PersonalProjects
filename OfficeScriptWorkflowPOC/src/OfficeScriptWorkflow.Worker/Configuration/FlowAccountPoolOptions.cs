namespace OfficeScriptWorkflow.Worker.Configuration;

/// <summary>
/// Pool of Power Automate service accounts used to distribute load when daily
/// action limits are approached on any single account.
///
/// Each entry in Accounts represents one M365 service account that owns its own
/// set of Power Automate flows. The flows are identical in logic but backed by a
/// different M365 user's connection (and therefore a separate 40,000 action/day quota).
///
/// When not configured (Accounts is empty), the WorkbookRegistry flow URLs are used
/// directly — single-account mode. Enable the pool only when batch optimisation alone
/// is insufficient to stay within the single-account daily action limit.
/// </summary>
public class FlowAccountPoolOptions
{
    public List<FlowAccountEntry> Accounts { get; set; } = [];
}

public class FlowAccountEntry
{
    /// <summary>Identifier for logging and quota tracking (e.g. "svc-os-01").</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>
    /// M365 user principal name of this service account
    /// (e.g. "svc-os-01@contoso.onmicrosoft.com").
    /// Used by scripts/Deploy-OfficeScripts.ps1 to locate the account's OneDrive
    /// when deploying Office Scripts to "My Scripts".
    /// Not used at runtime by the Worker Service.
    /// </summary>
    public string Upn { get; set; } = string.Empty;

    /// <summary>
    /// Maximum daily actions before this account is considered exhausted.
    /// Per-user Premium = 40,000. Subtract a safety buffer (e.g. use 36,000).
    /// </summary>
    public int DailyActionLimit { get; set; } = 36_000;

    // Per-account flow URLs (SAS-signed, store in Key Vault)
    public string InsertRangeFlowUrl { get; set; } = string.Empty;
    public string UpdateRangeFlowUrl { get; set; } = string.Empty;
    public string ExtractRangeFlowUrl { get; set; } = string.Empty;
    public string BatchOperationFlowUrl { get; set; } = string.Empty;
}
