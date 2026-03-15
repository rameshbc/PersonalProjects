using Application.Interfaces;
using Calculation.Engine;
using Calculation.Pipeline;
using Calculation.Rules;
using Calculation.Rules.Config;
using Calculation.Strategies;
using Calculation.Strategies.Local;
using Calculation.Strategies.States;
using Domain.Enums;
using Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace WorkerService;

/// <summary>
/// DI registration for the state modification calculation service.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSaltCalculationServices(
        this IServiceCollection services,
        string rulesRootDirectory)
    {
        // ── Rules ──────────────────────────────────────────────────────────

        // Code-based rules (complex logic that can't be expressed purely in config)
        services.AddTransient<IModificationRule, GiltiInclusionRule>();
        services.AddTransient<IModificationRule, GiltiDeductionRule>();
        services.AddTransient<IModificationRule, SubpartFInclusionRule>();
        services.AddTransient<IModificationRule, FdiiDeductionRule>();
        services.AddTransient<IModificationRule, InterestExpenseAddbackRule>();
        services.AddTransient<IModificationRule, BonusDepreciationAddbackRule>();
        services.AddTransient<IModificationRule, Section965InclusionRule>();

        // Config-driven rules loaded from JSON files
        services.AddSingleton<RuleConfigurationLoader>();
        services.AddSingleton<IReadOnlyList<RuleDefinition>>(sp =>
        {
            var loader = sp.GetRequiredService<RuleConfigurationLoader>();
            return loader.LoadAll(rulesRootDirectory);
        });
        services.AddTransient<IEnumerable<ConfigurableModificationRule>>(sp =>
        {
            var defs = sp.GetRequiredService<IReadOnlyList<RuleDefinition>>();
            return defs
                .Where(d => d.FormulaType != RuleFormulaType.CodeBased)
                .Select(d => new ConfigurableModificationRule(d));
        });

        // ── Jurisdiction strategies ────────────────────────────────────────

        services.AddTransient<DefaultJurisdictionStrategy>();

        // Register year-agnostic state strategies (key = JurisdictionCode string)
        services.AddKeyedTransient<IJurisdictionModificationStrategy,
            CaliforniaModificationStrategy>("CA");
        services.AddKeyedTransient<IJurisdictionModificationStrategy,
            NewYorkModificationStrategy>("NY");
        services.AddKeyedTransient<IJurisdictionModificationStrategy,
            IllinoisModificationStrategy>("IL");

        // Register local strategies
        services.AddKeyedTransient<IJurisdictionModificationStrategy,
            NewYorkCityModificationStrategy>("NYC");

        // Strategy factory
        services.AddSingleton<IJurisdictionStrategyFactory, JurisdictionStrategyFactory>();

        // Apportionment data (swap out in tests via fake)
        services.AddScoped<IApportionmentDataProvider, Infrastructure.Repositories.ApportionmentDataRepository>();

        // ── Calculation pipeline ───────────────────────────────────────────

        services.AddScoped<ICalculationStage, PreApportionmentStage>();
        services.AddScoped<ICalculationStage, ApportionmentStage>();
        services.AddScoped<ICalculationStage, PostApportionmentStage>();
        services.AddScoped<ICalculationPipeline, CalculationPipeline>();
        services.AddScoped<IModificationCalculationEngine, ModificationCalculationEngine>();

        // ── Infrastructure ─────────────────────────────────────────────────

        services.AddSingleton<ICalculationJobQueue, InMemoryCalculationJobQueue>();

        return services;
    }
}
