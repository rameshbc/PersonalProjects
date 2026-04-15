using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using SharepointDocManager.Api.Hubs;
using SharepointDocManager.Api.Middleware;
using SharepointDocManager.Application.Handlers;
using SharepointDocManager.Application.Services;
using SharepointDocManager.Core.Interfaces;
using SharepointDocManager.Infrastructure.Adapters;
using SharepointDocManager.Infrastructure.Excel;
using SharepointDocManager.Infrastructure.Graph;
using SharepointDocManager.Infrastructure.Permissions;
using SharepointDocManager.Infrastructure.Persistence;
using SharepointDocManager.Infrastructure.Resilience;

// ── Serilog bootstrap ──────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console(new CompactJsonFormatter())
        .Enrich.FromLogContext());

    // ── EF Core + Repository ──────────────────────────────────────────────────
    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
    builder.Services.AddScoped<IClientSiteRepository, ClientSiteRepository>();

    // ── Graph client (singleton) ──────────────────────────────────────────────
    builder.Services.AddSingleton<GraphClientFactory>();
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<GraphClientFactory>().Create());

    // ── Throttling handler (in HttpClient pipeline) ───────────────────────────
    builder.Services.AddTransient<GraphThrottlingHandler>();

    // ── Polly resilience pipelines ────────────────────────────────────────────
    // TODO: Re-add Polly v8 resilience pipelines when proper configuration is available
    // builder.Services.AddResiliencePipeline("Standard", ResiliencePipelineRegistry.ConfigureStandard);
    // builder.Services.AddResiliencePipeline("Gold",     ResiliencePipelineRegistry.ConfigureGold);

    // ── Infrastructure services ───────────────────────────────────────────────
    builder.Services.AddSingleton<GraphBatchExecutor>();
    builder.Services.AddSingleton<GraphUploadSessionManager>();
    builder.Services.AddSingleton<BulkheadPolicy>();

    // ── Keyed adapters — StorageAdapterResolver selects by key ────────────────
    builder.Services.AddKeyedSingleton<IDocumentStorageAdapter, SharePointAdapter>("SP");
    builder.Services.AddKeyedSingleton<IDocumentStorageAdapter, SharePointEmbeddedAdapter>("SPE");

    // ── Keyed permission services ─────────────────────────────────────────────
    builder.Services.AddKeyedSingleton<IPermissionService, SharePointPermissionService>("SP");
    builder.Services.AddKeyedSingleton<IPermissionService, SpePermissionService>("SPE");

    // ── Excel workbook service ────────────────────────────────────────────────
    builder.Services.AddSingleton<IExcelWorkbookService, ExcelWorkbookService>();

    // ── Application services ──────────────────────────────────────────────────
    builder.Services.AddSingleton<StorageAdapterResolver>();
    builder.Services.AddSingleton<FolderProvisioningService>();
    builder.Services.AddSingleton<DocumentOrchestrationService>();

    // ── Command/query handlers ────────────────────────────────────────────────
    builder.Services.AddTransient<CreateFolderStructureHandler>();
    builder.Services.AddTransient<UploadDocumentHandler>();
    builder.Services.AddTransient<BatchUploadDocumentsHandler>();
    builder.Services.AddTransient<GrantFolderPermissionsHandler>();
    builder.Services.AddTransient<ProvisionClientSiteHandler>();
    builder.Services.AddTransient<GetDocumentListHandler>();
    builder.Services.AddTransient<GetVersionHistoryHandler>();
    builder.Services.AddTransient<GetOnlineEditUrlHandler>();

    // ── ASP.NET Core ──────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddSignalR();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // ── OpenTelemetry tracing ─────────────────────────────────────────────────
    // TODO: Re-add OpenTelemetry instrumentation when dependencies are properly configured
    // builder.Services.AddOpenTelemetry()
    //     .WithTracing(tracing => tracing
    //         .AddAspNetCoreInstrumentation()
    //         .AddHttpClientInstrumentation());

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging();
    app.UseClientContext();      // Reads X-Client-Id header
    app.UseAuthorization();
    app.MapControllers();
    app.MapHub<UploadProgressHub>("/hubs/upload-progress");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed.");
}
finally
{
    Log.CloseAndFlush();
}
