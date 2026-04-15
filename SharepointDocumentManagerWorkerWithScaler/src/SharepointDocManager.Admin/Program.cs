using Serilog;
using Serilog.Formatting.Compact;
using SharepointDocManager.Admin.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console(new CompactJsonFormatter())
        .Enrich.FromLogContext());

    // ── Blazor Server ─────────────────────────────────────────────────────────
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // ── Admin API client — calls the Api project backend ──────────────────────
    builder.Services.AddScoped<AdminApiClient>();
    builder.Services.AddHttpClient<AdminApiClient>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["Api:BaseUrl"]
            ?? throw new InvalidOperationException("Api:BaseUrl is not configured."));
    });

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
        app.UseHsts();

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseAntiforgery();

    app.MapRazorComponents<SharepointDocManager.Admin.App>()
        .AddInteractiveServerRenderMode();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Admin portal startup failed.");
}
finally
{
    Log.CloseAndFlush();
}
