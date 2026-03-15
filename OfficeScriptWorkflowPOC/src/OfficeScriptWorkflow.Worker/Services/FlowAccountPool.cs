using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OfficeScriptWorkflow.Worker.Configuration;

namespace OfficeScriptWorkflow.Worker.Services;

/// <summary>
/// Thread-safe, quota-aware service account pool.
///
/// Quota counters and exhaustion flags reset automatically at midnight UTC
/// (checked lazily on each GetNext() call — no background timer needed).
/// </summary>
public sealed class FlowAccountPool : IFlowAccountPool
{
    private readonly IReadOnlyList<FlowAccountEntry> _accounts;
    private readonly ConcurrentDictionary<string, AccountState> _state;
    private readonly ILogger<FlowAccountPool> _logger;
    private int _roundRobinIndex = -1;

    public FlowAccountPool(
        IOptions<FlowAccountPoolOptions> options,
        ILogger<FlowAccountPool> logger)
    {
        _logger = logger;
        _accounts = options.Value.Accounts.AsReadOnly();
        _state = new ConcurrentDictionary<string, AccountState>(
            _accounts.ToDictionary(a => a.AccountId, _ => new AccountState()));

        _logger.LogInformation(
            "FlowAccountPool initialised with {Count} account(s): [{Ids}]",
            _accounts.Count,
            string.Join(", ", _accounts.Select(a => a.AccountId)));
    }

    public bool HasCapacity => _accounts.Any(a => !GetState(a.AccountId).IsExhausted(a.DailyActionLimit));

    public FlowAccountEntry GetNext()
    {
        if (_accounts.Count == 0)
            throw new InvalidOperationException(
                "FlowAccountPool has no accounts configured. " +
                "Either add accounts to FlowAccountPool:Accounts or disable the pool.");

        // Up to _accounts.Count attempts to find a non-exhausted account.
        for (int attempt = 0; attempt < _accounts.Count; attempt++)
        {
            var index = (int)((uint)Interlocked.Increment(ref _roundRobinIndex) % (uint)_accounts.Count);
            var account = _accounts[index];
            var state = GetState(account.AccountId);

            if (!state.IsExhausted(account.DailyActionLimit))
            {
                _logger.LogDebug(
                    "Pool routed to account {AccountId}. Actions today: {Actions}/{Limit}",
                    account.AccountId, state.ActionsToday, account.DailyActionLimit);
                return account;
            }

            _logger.LogDebug(
                "Account {AccountId} is exhausted ({Actions}/{Limit}). Trying next.",
                account.AccountId, state.ActionsToday, account.DailyActionLimit);
        }

        throw new QuotaExceededException();
    }

    public void MarkExhausted(string accountId)
    {
        var state = GetState(accountId);
        state.ExhaustedUntil = NextMidnightUtc();
        _logger.LogWarning(
            "Account {AccountId} marked exhausted (daily quota hit). Will reset at {ResetTime} UTC.",
            accountId, state.ExhaustedUntil);
    }

    public void RecordActions(string accountId, int actionCount = 3)
    {
        var state = GetState(accountId);
        Interlocked.Add(ref state.ActionsTodayField, actionCount);
    }

    private AccountState GetState(string accountId) =>
        _state.GetOrAdd(accountId, _ => new AccountState());

    private static DateTimeOffset NextMidnightUtc()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero)
            .AddDays(1);
    }

    private sealed class AccountState
    {
        // Interlocked-accessible backing field
        public int ActionsTodayField;
        public int ActionsToday => ActionsTodayField;
        public DateTimeOffset ExhaustedUntil { get; set; } = DateTimeOffset.MinValue;

        public bool IsExhausted(int limit)
        {
            // Reset if we're past the exhaustion window (new UTC day).
            if (ExhaustedUntil != DateTimeOffset.MinValue && DateTimeOffset.UtcNow >= ExhaustedUntil)
            {
                Interlocked.Exchange(ref ActionsTodayField, 0);
                ExhaustedUntil = DateTimeOffset.MinValue;
            }
            return DateTimeOffset.UtcNow < ExhaustedUntil || ActionsToday >= limit;
        }
    }
}
