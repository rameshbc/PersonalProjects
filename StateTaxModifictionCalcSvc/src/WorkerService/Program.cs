using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkerService;
using WorkerService.Processors;
using WorkerService.Workers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Rules JSON directory — configurable via appsettings
        var rulesDir = context.Configuration["RulesDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "Config", "Rules");

        // Register SALT calculation services
        services.AddSaltCalculationServices(rulesDir);

        // MediatR (commands + queries)
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(Application.Commands.TriggerCalculationCommand).Assembly));

        // Worker and processor (scoped — new instance per job)
        services.AddScoped<CalculationJobProcessor>();
        services.AddHostedService<CalculationWorker>();

        // Infrastructure repositories (swap for EF-backed implementations)
        // services.AddDbContext<TaxDbContext>(...)
        // services.AddScoped<ICalculationJobRepository, SqlCalculationJobRepository>();
        // services.AddScoped<IEntityRepository, SqlEntityRepository>();
        // services.AddScoped<IJurisdictionRepository, SqlJurisdictionRepository>();
        // services.AddScoped<IModificationCategoryRepository, SqlModificationCategoryRepository>();
        // services.AddScoped<IStateModificationRepository, SqlStateModificationRepository>();
        // services.AddScoped<IReportLineRepository, SqlReportLineRepository>();
    })
    .Build();

await host.RunAsync();
