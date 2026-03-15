using Calculation.Strategies;
using Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Calculation.Tests.Unit.Strategies;

/// <summary>
/// Tests for JurisdictionStrategyFactory — specifically the tax-year waterfall
/// resolution logic that ensures a new strategy registered for 2025 does not
/// affect 2024 or earlier calculations.
/// </summary>
public sealed class JurisdictionStrategyFactoryTests
{
    // ── Exact year match ───────────────────────────────────────────────────

    [Fact]
    public void Returns_exact_year_strategy_when_registered()
    {
        var (factory, strats) = Build("CA:2024", "CA:2023", "CA");

        factory.GetStrategy(JurisdictionCode.CA, 2024).Should().BeSameAs(strats["CA:2024"]);
    }

    [Fact]
    public void Year_exact_match_is_isolated_from_adjacent_years()
    {
        var (factory, strats) = Build("CA:2024", "CA:2023", "CA");

        factory.GetStrategy(JurisdictionCode.CA, 2023).Should().BeSameAs(strats["CA:2023"]);
        factory.GetStrategy(JurisdictionCode.CA, 2024).Should().BeSameAs(strats["CA:2024"]);
    }

    // ── Year waterfall ─────────────────────────────────────────────────────

    [Fact]
    public void Falls_back_to_nearest_prior_year_when_exact_year_not_registered()
    {
        // 2026 and 2025 not registered → walks back and finds CA:2024.
        var (factory, strats) = Build("CA:2024", "CA:2023", "CA");

        factory.GetStrategy(JurisdictionCode.CA, 2026)
            .Should().BeSameAs(strats["CA:2024"],
                "2026 and 2025 not registered; 2024 is the nearest registered prior year");
    }

    [Fact]
    public void Falls_back_to_code_only_strategy_when_no_year_match()
    {
        // 2015 is before any registered CA year → falls back to "CA" code-only entry.
        var (factory, strats) = Build("CA:2024", "CA:2023", "CA");

        factory.GetStrategy(JurisdictionCode.CA, 2015)
            .Should().BeSameAs(strats["CA"]);
    }

    [Fact]
    public void Falls_back_to_default_strategy_when_jurisdiction_not_registered()
    {
        var (factory, strats) = Build("CA:2024", "CA");

        // TX has no registration at all → DefaultJurisdictionStrategy
        var strategy = factory.GetStrategy(JurisdictionCode.TX, 2024);

        // DefaultJurisdictionStrategy is registered separately; just assert it doesn't throw
        // and returns something (not CA's strategy).
        strategy.Should().NotBeNull();
        strategy.Should().NotBeSameAs(strats["CA:2024"]);
        strategy.Should().NotBeSameAs(strats["CA"]);
    }

    // ── Registration isolation ─────────────────────────────────────────────

    [Fact]
    public void Adding_CA_2025_strategy_does_not_change_resolution_for_2024()
    {
        // This is the key invariant: deploying a new tax year must not displace
        // existing year resolutions.
        var (factory, strats) = Build("CA:2025", "CA:2024", "CA:2023", "CA");

        factory.GetStrategy(JurisdictionCode.CA, 2023).Should().BeSameAs(strats["CA:2023"]);
        factory.GetStrategy(JurisdictionCode.CA, 2024).Should().BeSameAs(strats["CA:2024"]);
        factory.GetStrategy(JurisdictionCode.CA, 2025).Should().BeSameAs(strats["CA:2025"]);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a JurisdictionStrategyFactory from a set of DI keys.
    /// Each key maps to a unique NSubstitute mock so tests can assert identity.
    /// "DEFAULT" is registered as DefaultJurisdictionStrategy.
    /// </summary>
    private static (JurisdictionStrategyFactory factory, Dictionary<string, IJurisdictionModificationStrategy> strats)
        Build(params string[] keys)
    {
        var services = new ServiceCollection();
        var strats = new Dictionary<string, IJurisdictionModificationStrategy>();

        foreach (var key in keys)
        {
            var mock = Substitute.For<IJurisdictionModificationStrategy>();
            strats[key] = mock;
            services.AddKeyedSingleton<IJurisdictionModificationStrategy>(key, mock);
        }

        // DefaultJurisdictionStrategy is always required by the factory's step 4.
        services.AddSingleton<DefaultJurisdictionStrategy>(sp =>
            new DefaultJurisdictionStrategy(
                [],
                Substitute.For<Calculation.Strategies.IApportionmentDataProvider>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultJurisdictionStrategy>.Instance));

        var provider = services.BuildServiceProvider();
        var factory  = new JurisdictionStrategyFactory(provider);

        return (factory, strats);
    }
}
