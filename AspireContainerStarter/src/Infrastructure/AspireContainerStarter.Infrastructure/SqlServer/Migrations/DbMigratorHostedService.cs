using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AspireContainerStarter.Infrastructure.SqlServer.Migrations;

/// <summary>
/// Runs EF Core migrations for <typeparamref name="TContext"/> on startup,
/// before the Service Bus processor begins accepting messages.
///
/// Register this <em>before</em> the processor hosted service so ordering is guaranteed:
/// <code>
///   builder.Services.AddHostedService&lt;DbMigratorHostedService&lt;CalculationDbContext&gt;&gt;();
///   builder.Services.AddHostedService&lt;ServiceBusProcessorHostedService&lt;Calc1JobMessage&gt;&gt;();
/// </code>
/// </summary>
public sealed class DbMigratorHostedService<TContext> : IHostedService
    where TContext : DbContext
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DbMigratorHostedService<TContext>> _logger;

    public DbMigratorHostedService(
        IServiceProvider services,
        ILogger<DbMigratorHostedService<TContext>> logger)
    {
        _services = services;
        _logger   = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Applying EF Core migrations for {Context}…", typeof(TContext).Name);
        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        await context.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Migrations applied for {Context}.", typeof(TContext).Name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
