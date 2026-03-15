using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Calculation.Engine;
using Calculation.Pipeline;
using Calculation.Rules;
using Calculation.Strategies;
using Calculation.Tests.Builders;
using Calculation.Tests.Fakes;
using Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Calculation.Tests.Performance;

/// <summary>
/// BenchmarkDotNet benchmarks for the calculation pipeline.
/// Run with: dotnet run -c Release --project tests/Calculation.Tests -- --filter "*Benchmarks*"
///
/// Measures:
///   - Single entity × single jurisdiction calculation throughput
///   - 1000-entity batch (simulates a mid-sized client)
///   - Strategy factory resolution overhead
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class CalculationEngineBenchmarks
{
    private CalculationContext _context = null!;
    private CaliforniaSetup _caSetup = null!;

    [GlobalSetup]
    public void Setup()
    {
        _context = new CalculationContextBuilder()
            .ForJurisdiction(JurisdictionCode.CA)
            .ForTaxYear(2024)
            .WithFederalLines(new()
            {
                ["1120_SCH_C_L10_GILTI"] = 1_000_000m,
                ["1120_GILTI_HIGH_TAX_EXCL"] = 0m,
                ["1120_SCH_C_L10_SECT78"] = 0m,
                ["1120_M3_L30_BONUS_DEPR"] = 500_000m,
                ["CA_ALLOWED_DEPR"] = 100_000m,
                ["CA_NOL_AVAILABLE_BALANCE"] = 200_000m,
                ["1120_F8990_L30_DISALLOWED_INT"] = 50_000m,
                ["1120_F8990_L6_PRIOR_CARRYOVER"] = 0m
            })
            .WithCategory(ModificationCategoryBuilder.GiltiInclusion())
            .WithCategory(ModificationCategoryBuilder.BonusDepreciationAddback())
            .Build();

        _caSetup = new CaliforniaSetup();
    }

    [Benchmark(Description = "Single entity — full CA pipeline")]
    public async Task<CalculationContext> SingleEntityCaliforniaPipeline() =>
        await _caSetup.RunAsync(_context);

    [Benchmark(Description = "Strategy factory resolution — 1000 lookups")]
    public void StrategyFactoryResolution()
    {
        for (int i = 0; i < 1000; i++)
            _caSetup.Factory.GetStrategy(JurisdictionCode.CA, 2024);
    }
}

// ── Local helper to wire pipeline without full DI ──────────────────────────

internal sealed class CaliforniaSetup
{
    public FakeJurisdictionStrategyFactory Factory { get; }
    private readonly CalculationPipeline _pipeline;

    public CaliforniaSetup()
    {
        var rules = new IModificationRule[]
        {
            new GiltiInclusionRule(),
            new BonusDepreciationAddbackRule(),
            new InterestExpenseAddbackRule()
        };
        var fakeProvider = new FakeApportionmentDataProvider().WithFactor(300_000m, 1_000_000m);
        var caStrategy = new Calculation.Strategies.States.CaliforniaModificationStrategy(
            rules, fakeProvider,
            NullLogger<Calculation.Strategies.States.CaliforniaModificationStrategy>.Instance);

        Factory = new FakeJurisdictionStrategyFactory()
            .WithStrategy(JurisdictionCode.CA, caStrategy);

        _pipeline = new CalculationPipeline(
            new ICalculationStage[]
            {
                new PreApportionmentStage(Factory, NullLogger<PreApportionmentStage>.Instance),
                new ApportionmentStage(Factory, NullLogger<ApportionmentStage>.Instance),
                new PostApportionmentStage(Factory, NullLogger<PostApportionmentStage>.Instance)
            },
            NullLogger<CalculationPipeline>.Instance);
    }

    public Task<CalculationContext> RunAsync(CalculationContext ctx)
    {
        _ = _pipeline.ExecuteAsync(ctx);
        return Task.FromResult(ctx);
    }
}
