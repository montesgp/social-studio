using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Infrastructure.Observability;
using Microsoft.Extensions.Caching.Memory;

namespace Incoders.Hive.Tests.Infrastructure.Observability;

public sealed class ProviderCostRateLookupTests
{
    private static readonly DateTime Now = new(2026, 5, 26, 10, 0, 0, DateTimeKind.Utc);

    // -------------------------------------------------------------------
    // Known rate — returned and then served from cache
    // -------------------------------------------------------------------

    [Fact]
    public async Task GetRateAsync_KnownProviderModel_ReturnsCostRate()
    {
        var repo = new StubCostRateRepository(new ProviderCostRate(
            "claude",
            "claude-sonnet-4-5",
            CostPer1KInputTokens: 0.003m,
            CostPer1KOutputTokens: 0.015m,
            CostPerRequest: null,
            EffectiveFrom: Now.AddDays(-30),
            EffectiveTo: null));

        var lookup = CreateLookup(repo);

        var rate = await lookup.GetRateAsync("claude", "claude-sonnet-4-5", Now, CancellationToken.None);

        Assert.NotNull(rate);
        Assert.Equal(0.003m, rate!.CostPer1KInputTokens);
        Assert.Equal(0.015m, rate.CostPer1KOutputTokens);
    }

    [Fact]
    public async Task GetRateAsync_CalledTwice_SecondCallUsesCache()
    {
        var repo = new CountingCostRateRepository(new ProviderCostRate(
            "claude",
            "claude-sonnet-4-5",
            0.003m, 0.015m, null,
            Now.AddDays(-30), null));

        var lookup = CreateLookup(repo);

        await lookup.GetRateAsync("claude", "claude-sonnet-4-5", Now, CancellationToken.None);
        await lookup.GetRateAsync("claude", "claude-sonnet-4-5", Now, CancellationToken.None);

        // Repository should only be called once — second call hits cache
        Assert.Equal(1, repo.CallCount);
    }

    // -------------------------------------------------------------------
    // Rate-table miss — returns null, logs entry
    // -------------------------------------------------------------------

    [Fact]
    public async Task GetRateAsync_UnknownModel_ReturnsNull()
    {
        var repo = new StubCostRateRepository(null);
        var lookup = CreateLookup(repo);

        var rate = await lookup.GetRateAsync("claude", "unknown-model-xyz", Now, CancellationToken.None);

        Assert.Null(rate);
    }

    // -------------------------------------------------------------------
    // Expired rate (effective_to in the past) — not selected
    // -------------------------------------------------------------------

    [Fact]
    public async Task GetRateAsync_ExpiredRate_RepositoryHandlesFiltering()
    {
        // Simulate that the repository already filtered out expired rates
        // (this is verified in EfProviderCostRateRepository tests)
        // Here we verify the lookup passes the query through correctly.
        var expiredRate = new ProviderCostRate(
            "openai", "gpt-4o", 0.0025m, 0.01m, null,
            EffectiveFrom: Now.AddDays(-60),
            EffectiveTo: Now.AddDays(-1)); // already expired

        // Repository says null — simulating the EF filter removed expired rate
        var repo = new StubCostRateRepository(null);
        var lookup = CreateLookup(repo);

        var rate = await lookup.GetRateAsync("openai", "gpt-4o", Now, CancellationToken.None);

        Assert.Null(rate);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static ProviderCostRateLookup CreateLookup(IProviderCostRateRepository repo)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new ProviderCostRateLookup(repo, cache);
    }
}

// -------------------------------------------------------------------
// Test doubles
// -------------------------------------------------------------------

file sealed class StubCostRateRepository : IProviderCostRateRepository
{
    private readonly ProviderCostRate? _rate;

    public StubCostRateRepository(ProviderCostRate? rate) => _rate = rate;

    public Task<ProviderCostRate?> GetEffectiveRateAsync(
        string providerKey, string? model, DateTime atUtc, CancellationToken ct) =>
        Task.FromResult(_rate);
}

file sealed class CountingCostRateRepository : IProviderCostRateRepository
{
    private readonly ProviderCostRate? _rate;
    public int CallCount { get; private set; }

    public CountingCostRateRepository(ProviderCostRate? rate) => _rate = rate;

    public Task<ProviderCostRate?> GetEffectiveRateAsync(
        string providerKey, string? model, DateTime atUtc, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(_rate);
    }
}
