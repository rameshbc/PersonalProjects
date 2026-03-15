using AspireContainerStarter.Infrastructure.SqlServer.Extensions;
using AspireContainerStarter.Infrastructure.SqlServer.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AspireContainerStarter.Infrastructure.Tests.SqlServer;

public sealed class SqlServerExtensionsTests
{
    [Fact]
    public void AddAzureSqlWithManagedIdentity_RegistersDbContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAzureSqlWithManagedIdentity<TestDbContext>(
            connectionString: "Server=localhost;Database=test;TrustServerCertificate=True;");

        var provider = services.BuildServiceProvider();

        // DbContext should be resolvable.
        var ctx = provider.GetService<TestDbContext>();
        Assert.NotNull(ctx);
    }

    [Fact]
    public void AddAzureSqlWithManagedIdentity_ThrowsOnEmptyConnectionString()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddAzureSqlWithManagedIdentity<TestDbContext>(connectionString: ""));
    }

    [Fact]
    public void AddAzureSqlWithManagedIdentity_ConfiguresResilienceOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        SqlServerResilienceOptions? capturedOptions = null;

        services.AddAzureSqlWithManagedIdentity<TestDbContext>(
            connectionString: "Server=localhost;Database=test;TrustServerCertificate=True;",
            configureResilience: opts =>
            {
                opts.MaxRetryAttempts = 2;
                capturedOptions = opts;
            });

        Assert.NotNull(capturedOptions);
        Assert.Equal(2, capturedOptions.MaxRetryAttempts);
    }

    [Fact]
    public void AddAzureSqlWithManagedIdentity_RegistersHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAzureSqlWithManagedIdentity<TestDbContext>(
            connectionString: "Server=localhost;Database=test;TrustServerCertificate=True;");

        // Verify IHealthCheckService is registered (implicitly by AddHealthChecks).
        var provider   = services.BuildServiceProvider();
        var hcService  = provider.GetService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        Assert.NotNull(hcService);
    }

    // Minimal DbContext for registration testing.
    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}
