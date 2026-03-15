using Calculation.Strategies;
using Domain.Enums;

namespace Calculation.Tests.Fakes;

/// <summary>
/// Strategy factory for tests — returns pre-configured strategies or stubs.
/// Avoids the DI container in unit tests.
/// </summary>
public sealed class FakeJurisdictionStrategyFactory : IJurisdictionStrategyFactory
{
    private readonly Dictionary<string, IJurisdictionModificationStrategy> _strategies = [];
    private IJurisdictionModificationStrategy? _default;

    public FakeJurisdictionStrategyFactory WithStrategy(
        JurisdictionCode code,
        IJurisdictionModificationStrategy strategy,
        int? taxYear = null)
    {
        var key = taxYear.HasValue ? $"{code}:{taxYear}" : code.ToString();
        _strategies[key] = strategy;
        return this;
    }

    public FakeJurisdictionStrategyFactory WithDefault(IJurisdictionModificationStrategy strategy)
    {
        _default = strategy;
        return this;
    }

    public IJurisdictionModificationStrategy GetStrategy(JurisdictionCode code, int taxYear)
    {
        // Exact year match
        if (_strategies.TryGetValue($"{code}:{taxYear}", out var exact)) return exact;
        // Code-only match
        if (_strategies.TryGetValue(code.ToString(), out var generic)) return generic;
        // Supplied default
        if (_default is not null) return _default;

        throw new InvalidOperationException(
            $"No strategy configured for {code}:{taxYear} in FakeJurisdictionStrategyFactory. " +
            "Call .WithStrategy() or .WithDefault() in your test setup.");
    }
}
