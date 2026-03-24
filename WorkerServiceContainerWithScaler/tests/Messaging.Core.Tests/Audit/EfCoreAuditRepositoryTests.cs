namespace Messaging.Core.Tests.Audit;

using Messaging.Core.Audit.DbContext;
using Messaging.Core.Audit.Models;
using Messaging.Core.Audit.Repositories;
using Messaging.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class EfCoreAuditRepositoryTests : IDisposable
{
    // InMemory factory — used for tests that don't call ExecuteUpdateAsync
    private readonly IDbContextFactory<MessagingAuditDbContext> _inMemoryFactory;

    // SQLite in-process factory — used for tests that call ExecuteUpdateAsync,
    // which the EF Core InMemory provider does not support.
    private readonly SqliteConnection _sqliteConn;
    private readonly IDbContextFactory<MessagingAuditDbContext> _sqliteFactory;

    public EfCoreAuditRepositoryTests()
    {
        var inMemoryOptions = new DbContextOptionsBuilder<MessagingAuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _inMemoryFactory = new TestDbContextFactory(inMemoryOptions);

        // Keep the connection open for the lifetime of the test so the in-memory SQLite
        // database survives across multiple CreateDbContext() calls.
        _sqliteConn = new SqliteConnection("DataSource=:memory:");
        _sqliteConn.Open();
        var sqliteOptions = new DbContextOptionsBuilder<MessagingAuditDbContext>()
            .UseSqlite(_sqliteConn)
            .Options;
        _sqliteFactory = new TestDbContextFactory(sqliteOptions);

        // Create schema once
        using var ctx = _sqliteFactory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    [Fact]
    public async Task InsertAsync_PersistsEntry()
    {
        var repo  = new EfCoreAuditRepository(_inMemoryFactory, NullLogger<EfCoreAuditRepository>.Instance);
        var entry = BuildEntry(MessageStatus.Queued);

        await repo.InsertAsync(entry);

        await using var db = await _inMemoryFactory.CreateDbContextAsync();
        var stored = await db.MessageAuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("test-client", stored!.ClientId);
        Assert.Equal(MessageStatus.Queued, stored.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesStatus()
    {
        var repo  = new EfCoreAuditRepository(_sqliteFactory, NullLogger<EfCoreAuditRepository>.Instance);
        var entry = BuildEntry(MessageStatus.Queued);
        await repo.InsertAsync(entry);

        await repo.UpdateStatusAsync(entry.Id, MessageStatus.Published, null);

        await using var db = await _sqliteFactory.CreateDbContextAsync();
        var stored = await db.MessageAuditLogs.FindAsync(entry.Id);
        Assert.Equal(MessageStatus.Published, stored!.Status);
    }

    private static MessageAuditLog BuildEntry(MessageStatus status) => new()
    {
        ClientId        = "test-client",
        ServiceName     = "TestService",
        HostName        = "localhost",
        OperationType   = "Publish",
        DestinationType = DestinationType.Queue,
        DestinationName = "test-queue",
        MessageId       = Guid.NewGuid().ToString(),
        Status          = status,
        CreatedAt       = DateTime.UtcNow,
        UpdatedAt       = DateTime.UtcNow
    };

    public void Dispose() => _sqliteConn.Dispose();

    private sealed class TestDbContextFactory : IDbContextFactory<MessagingAuditDbContext>
    {
        private readonly DbContextOptions<MessagingAuditDbContext> _options;
        public TestDbContextFactory(DbContextOptions<MessagingAuditDbContext> options) => _options = options;
        public MessagingAuditDbContext CreateDbContext() => new(_options);
        public Task<MessagingAuditDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new MessagingAuditDbContext(_options));
    }
}
