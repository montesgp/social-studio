using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// TDD tests for WU-3 step 3.11: BackgroundStrategyResolver.
/// Refs: BSP-05, BSP-06; design §4.2; tasks 3.11.
/// </summary>
public sealed class BackgroundStrategyResolverTests
{
    // -----------------------------------------------------------------------
    // null / Unset → LegacyTierStrategy (BSP-05)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NullPolicy_ReturnsLegacyTierStrategy()
    {
        var resolver = BuildDefaultResolver();

        var result = resolver.Resolve(null);

        Assert.IsType<LegacyTierStrategy>(result);
    }

    [Fact]
    public void Resolve_UnsetPolicy_ReturnsLegacyTierStrategy()
    {
        var resolver = BuildDefaultResolver();

        var result = resolver.Resolve(BackgroundSourcingPolicy.Unset);

        Assert.IsType<LegacyTierStrategy>(result);
    }

    // -----------------------------------------------------------------------
    // Known cases A/B/C
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ClientDefinedSet_ReturnsClientDefinedSetStrategy()
    {
        var resolver = BuildDefaultResolver();

        var result = resolver.Resolve(BackgroundSourcingPolicy.ClientDefinedSet);

        Assert.IsType<ClientDefinedSetStrategy>(result);
    }

    [Fact]
    public void Resolve_AiPalette_ReturnsAiPaletteStrategy()
    {
        var resolver = BuildDefaultResolver();

        var result = resolver.Resolve(BackgroundSourcingPolicy.AiPalette);

        Assert.IsType<AiPaletteStrategy>(result);
    }

    [Fact]
    public void Resolve_ClientAesthetic_ReturnsClientAestheticStrategy()
    {
        var resolver = BuildDefaultResolver();

        var result = resolver.Resolve(BackgroundSourcingPolicy.ClientAesthetic);

        Assert.IsType<ClientAestheticStrategy>(result);
    }

    // -----------------------------------------------------------------------
    // OCP test: CASE D strategy resolves without modifying A/B/C (BSP-06)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_FakeCaseD_ResolvesFakeDStrategy_WithoutTouchingABC()
    {
        // Build a resolver that includes A/B/C + a fake CASE-D strategy
        // The fake uses a future BackgroundSourcingPolicy value; we model it with a stub
        var fakeDStrategy = new FakeCaseDStrategy();

        var strategies = new IBackgroundSourcingStrategy[]
        {
            new LegacyTierStrategy(),
            new ClientDefinedSetStrategy(),
            new AiPaletteStrategy(),
            new ClientAestheticStrategy(NullLogger<ClientAestheticStrategy>.Instance),
            fakeDStrategy,
        };

        var resolver = new BackgroundStrategyResolver(strategies, new LegacyTierStrategy());

        // Resolving the fake D policy returns the fake strategy (not A/B/C)
        var result = resolver.Resolve(fakeDStrategy.Policy);

        Assert.Same(fakeDStrategy, result);
    }

    [Fact]
    public void Resolve_UnknownPolicy_FallsBackToLegacy()
    {
        // A policy value that has no registered strategy → legacy fallback
        var resolver = BuildDefaultResolver();

        // Use a policy value that is not among the defaults (simulate unknown)
        // We can cast an int to the enum to simulate an unregistered policy
        var unknownPolicy = (BackgroundSourcingPolicy)99;
        var result = resolver.Resolve(unknownPolicy);

        Assert.IsType<LegacyTierStrategy>(result);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static BackgroundStrategyResolver BuildDefaultResolver()
    {
        var strategies = new IBackgroundSourcingStrategy[]
        {
            new LegacyTierStrategy(),
            new ClientDefinedSetStrategy(),
            new AiPaletteStrategy(),
            new ClientAestheticStrategy(NullLogger<ClientAestheticStrategy>.Instance),
        };

        return new BackgroundStrategyResolver(strategies, new LegacyTierStrategy());
    }
}

/// <summary>
/// Stub CASE D strategy for OCP extensibility test (BSP-06-a).
/// Uses a fabricated policy enum value to simulate a future case being registered.
/// No A/B/C files are modified to add this strategy.
/// </summary>
file sealed class FakeCaseDStrategy : IBackgroundSourcingStrategy
{
    // Simulate a future enum value (cast an int outside the current enum range)
    public BackgroundSourcingPolicy Policy => (BackgroundSourcingPolicy)4;

    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request)
    {
        return new CarouselBackgroundPlan(
            Policy,
            new BackgroundSelection[] { new BackgroundSelection.Creative() },
            request.SeedToken);
    }
}
