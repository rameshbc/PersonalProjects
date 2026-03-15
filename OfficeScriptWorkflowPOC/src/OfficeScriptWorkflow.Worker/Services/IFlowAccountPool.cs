using OfficeScriptWorkflow.Worker.Configuration;

namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Distributes Power Automate flow calls across multiple service accounts to
/// stay within each account's daily action quota (40,000 per user Premium).
///
/// Selection strategy:
///   1. Round-robin across non-exhausted accounts.
///   2. When a 429 (TooManyRequests) response is received from Power Automate,
///      the caller marks that account exhausted for the rest of the UTC day via
///      MarkExhausted(accountId). The pool automatically routes to the next account.
///   3. If all accounts are exhausted, GetNext() throws QuotaExceededException
///      so the caller can dead-letter or back off.
///
/// In single-account mode (pool is empty / not configured), calls are routed
/// directly through the WorkbookRegistry flow URLs — this class is a no-op.
/// </summary>
public interface IFlowAccountPool
{
    /// <summary>
    /// Returns the next available account entry for routing a flow call.
    /// Throws <see cref="QuotaExceededException"/> if all accounts are exhausted.
    /// </summary>
    FlowAccountEntry GetNext();

    /// <summary>
    /// Marks an account as exhausted until midnight UTC.
    /// Called when Power Automate returns HTTP 429 with a daily-quota reason.
    /// </summary>
    void MarkExhausted(string accountId);

    /// <summary>Returns true if at least one account has remaining capacity.</summary>
    bool HasCapacity { get; }

    /// <summary>Increments the action counter for an account (3 per flow call).</summary>
    void RecordActions(string accountId, int actionCount = 3);
}

public sealed class QuotaExceededException : Exception
{
    public QuotaExceededException()
        : base("All accounts in the flow account pool have exhausted their daily action quota.") { }
}
