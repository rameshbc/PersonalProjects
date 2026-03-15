using Microsoft.Extensions.Options;
using OfficeScriptWorkflow.Worker.Clients;
using OfficeScriptWorkflow.Worker.Configuration;
using OfficeScriptWorkflow.Worker.Infrastructure.Http;
using OfficeScriptWorkflow.Worker.Infrastructure.Resilience;
using OfficeScriptWorkflow.Worker.Services;

namespace OfficeScriptWorkflow.Worker.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Configuration ───────────────────────────────────────────────────────────
        services
            .AddOptions<WorkbookRegistryOptions>()
            .BindConfiguration("WorkbookRegistry")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<ConcurrencyOptions>()
            .BindConfiguration("Concurrency");

        services
            .AddOptions<ResilienceConfiguration>()
            .BindConfiguration("Resilience");

        services
            .AddOptions<ServiceBusConfiguration>()
            .BindConfiguration("ServiceBus");

        services
            .AddOptions<FlowAccountPoolOptions>()
            .BindConfiguration("FlowAccountPool");

        // ── Core services ───────────────────────────────────────────────────────────
        services.AddSingleton<IWorkbookRegistry, WorkbookRegistry>();
        services.AddSingleton<IFlowAccountPool, FlowAccountPool>();
        services.AddSingleton<IOperationResultStore, InMemoryOperationResultStore>();
        services.AddScoped<IExcelWorkbookService, ExcelWorkbookService>();

        // ── Operation queue (in-memory or Service Bus, selected by config) ──────────
        var useServiceBus = configuration.GetValue<bool>("ServiceBus:UseServiceBus");
        if (useServiceBus)
            services.AddSingleton<IOperationQueue, AzureServiceBusOperationQueue>();
        else
            services.AddSingleton<IOperationQueue, InMemoryOperationQueue>();

        // ── HTTP pipeline ──────────────────────────────────────────────────────────
        // Handler registration order matters — outermost first in the pipeline:
        //   CircuitBreaker → Retry → AsyncPollingHandler → PowerAutomateRetryHandler → HttpClient

        services.AddTransient<PowerAutomateRetryHandler>();
        services.AddTransient<AsyncPollingHandler>();

        services
            .AddHttpClient<IPowerAutomateClient, PowerAutomateClient>()
            .AddHttpMessageHandler<PowerAutomateRetryHandler>()   // correlation ID + logging
            .AddHttpMessageHandler<AsyncPollingHandler>()          // 202 polling (long scripts)
            .AddPolicyHandler((sp, _) =>
            {
                var cfg = sp.GetRequiredService<IOptions<ResilienceConfiguration>>().Value;
                var log = sp.GetRequiredService<ILogger<PowerAutomateClient>>();
                return ResiliencePolicies.GetRetryPolicy(cfg.RetryCount, log);
            })
            .AddPolicyHandler((sp, _) =>
            {
                var cfg = sp.GetRequiredService<IOptions<ResilienceConfiguration>>().Value;
                var log = sp.GetRequiredService<ILogger<PowerAutomateClient>>();
                return ResiliencePolicies.GetCircuitBreakerPolicy(
                    cfg.CircuitBreakerThreshold,
                    TimeSpan.FromSeconds(cfg.CircuitBreakerBreakDurationSeconds),
                    log);
            });

        return services;
    }
}
