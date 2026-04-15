using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using SharepointDocManager.Application.Services;
using SharepointDocManager.Core.Interfaces;
using SharepointDocManager.Infrastructure.Adapters;
using SharepointDocManager.Infrastructure.Excel;
using SharepointDocManager.Infrastructure.Graph;
using SharepointDocManager.Infrastructure.Permissions;
using SharepointDocManager.Infrastructure.Persistence;
using SharepointDocManager.Infrastructure.Resilience;
using SharepointDocManager.Worker.Workers;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Services.AddSerilog(
        new LoggerConfiguration()
            .WriteTo.Console(new CompactJsonFormatter())
            .Enrich.FromLogContext()
            .CreateLogger());

    // ── EF Core + Repository ──────────────────────────────────────────────────
    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
    builder.Services.AddScoped<IClientSiteRepository, ClientSiteRepository>();

    // ── Graph client ──────────────────────────────────────────────────────────
    builder.Services.AddSingleton<GraphClientFactory>();
    builder.Services.AddSingleton(sp => sp.GetRequiredService<GraphClientFactory>().Create());
    builder.Services.AddTransient<GraphThrottlingHandler>();

    // ── Polly pipelines ───────────────────────────────────────────────────────
    // TODO: Re-add Polly v8 resilience pipelines when proper configuration is available
    // builder.Services.AddResiliencePipeline("Standard", ResiliencePipelineRegistry.ConfigureStandard);
    // builder.Services.AddResiliencePipeline("Gold",     ResiliencePipelineRegistry.ConfigureGold);

    // ── Infrastructure ────────────────────────────────────────────────────────
    builder.Services.AddSingleton<GraphBatchExecutor>();
    builder.Services.AddSingleton<GraphUploadSessionManager>();
    builder.Services.AddSingleton<BulkheadPolicy>();

    builder.Services.AddKeyedSingleton<IDocumentStorageAdapter, SharePointAdapter>("SP");
    builder.Services.AddKeyedSingleton<IDocumentStorageAdapter, SharePointEmbeddedAdapter>("SPE");
    builder.Services.AddKeyedSingleton<IPermissionService, SharePointPermissionService>("SP");
    builder.Services.AddKeyedSingleton<IPermissionService, SpePermissionService>("SPE");
    builder.Services.AddSingleton<IExcelWorkbookService, ExcelWorkbookService>();

    // ── Application services ──────────────────────────────────────────────────
    builder.Services.AddSingleton<StorageAdapterResolver>();
    builder.Services.AddSingleton<FolderProvisioningService>();
    builder.Services.AddSingleton<DocumentOrchestrationService>();

    // ── Background workers ────────────────────────────────────────────────────
    builder.Services.AddHostedService<BatchUploadWorker>();
    builder.Services.AddHostedService<PermissionSyncWorker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker host terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
