using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OfficeScriptWorkflow.Worker.Configuration;
using OfficeScriptWorkflow.Worker.Services;

namespace OfficeScriptWorkflow.Worker.Tests;

public class FlowAccountPoolTests
{
    private static FlowAccountPool Build(params FlowAccountEntry[] accounts)
    {
        var options = Options.Create(new FlowAccountPoolOptions { Accounts = [.. accounts] });
        return new FlowAccountPool(options, NullLogger<FlowAccountPool>.Instance);
    }

    private static FlowAccountEntry MakeAccount(string id, int dailyLimit = 40_000) =>
        new() { AccountId = id, DailyActionLimit = dailyLimit };

    // ── Empty pool ────────────────────────────────────────────────────────────

    [Fact]
    public void GetNext_EmptyPool_ThrowsInvalidOperation()
    {
        var pool = Build();
        Assert.Throws<InvalidOperationException>(() => pool.GetNext());
    }

    [Fact]
    public void HasCapacity_EmptyPool_ReturnsFalse()
    {
        var pool = Build();
        Assert.False(pool.HasCapacity);
    }

    // ── Single account ────────────────────────────────────────────────────────

    [Fact]
    public void GetNext_SingleAccount_ReturnsThatAccount()
    {
        var pool = Build(MakeAccount("svc-01"));

        var account = pool.GetNext();

        Assert.Equal("svc-01", account.AccountId);
    }

    [Fact]
    public void HasCapacity_FreshSingleAccount_ReturnsTrue()
    {
        var pool = Build(MakeAccount("svc-01"));
        Assert.True(pool.HasCapacity);
    }

    // ── Round-robin ───────────────────────────────────────────────────────────

    [Fact]
    public void GetNext_TwoAccounts_RoundRobins()
    {
        var pool = Build(MakeAccount("svc-01"), MakeAccount("svc-02"));

        var ids = Enumerable.Range(0, 4).Select(_ => pool.GetNext().AccountId).ToList();

        // Should alternate (or cover both accounts across 4 calls)
        Assert.Contains("svc-01", ids);
        Assert.Contains("svc-02", ids);
    }

    // ── Exhaustion ────────────────────────────────────────────────────────────

    [Fact]
    public void MarkExhausted_SingleAccount_GetNextThrowsQuota()
    {
        var pool = Build(MakeAccount("svc-01"));

        pool.MarkExhausted("svc-01");

        Assert.Throws<QuotaExceededException>(() => pool.GetNext());
    }

    [Fact]
    public void HasCapacity_AfterMarkExhausted_ReturnsFalse()
    {
        var pool = Build(MakeAccount("svc-01"));

        pool.MarkExhausted("svc-01");

        Assert.False(pool.HasCapacity);
    }

    [Fact]
    public void GetNext_FirstAccountExhausted_RoutesToSecond()
    {
        var pool = Build(MakeAccount("svc-01"), MakeAccount("svc-02"));

        pool.MarkExhausted("svc-01");

        // Should consistently return svc-02 now
        for (int i = 0; i < 5; i++)
        {
            var account = pool.GetNext();
            Assert.Equal("svc-02", account.AccountId);
        }
    }

    [Fact]
    public void GetNext_AllAccountsExhausted_ThrowsQuotaExceeded()
    {
        var pool = Build(MakeAccount("svc-01"), MakeAccount("svc-02"));

        pool.MarkExhausted("svc-01");
        pool.MarkExhausted("svc-02");

        Assert.Throws<QuotaExceededException>(() => pool.GetNext());
    }

    // ── Action counting ───────────────────────────────────────────────────────

    [Fact]
    public void RecordActions_ExceedsLimit_AccountBecomesExhausted()
    {
        var pool = Build(MakeAccount("svc-01", dailyLimit: 9));

        // 3 records × 3 actions each = 9 actions total → at the limit
        pool.RecordActions("svc-01", 3);
        pool.RecordActions("svc-01", 3);
        pool.RecordActions("svc-01", 3);

        // At or above limit — IsExhausted checks ActionsToday >= limit
        Assert.False(pool.HasCapacity);
    }

    [Fact]
    public void RecordActions_BelowLimit_AccountStillAvailable()
    {
        var pool = Build(MakeAccount("svc-01", dailyLimit: 40_000));

        pool.RecordActions("svc-01", 3);

        Assert.True(pool.HasCapacity);
        Assert.Equal("svc-01", pool.GetNext().AccountId);
    }

    // ── Thread safety (smoke test) ────────────────────────────────────────────

    [Fact]
    public void GetNext_ConcurrentCalls_NeverThrowsUnexpectedException()
    {
        var pool = Build(MakeAccount("svc-01"), MakeAccount("svc-02"));

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 100, _ =>
        {
            try { pool.GetNext(); }
            catch (QuotaExceededException) { /* acceptable */ }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        Assert.Empty(exceptions);
    }
}
