using Incoders.Hive.Application.Abstractions.Observability;
using Microsoft.Extensions.Caching.Memory;

namespace Incoders.Hive.Infrastructure.Observability;

/// <summary>
/// Caches provider cost-rate lookups for 5 minutes (sliding expiration) to avoid
/// a DB query on every provider call. Cache miss falls back to <see cref="IProviderCostRateRepository"/>.
/// </summary>
public sealed class ProviderCostRateLookup : IProviderCostRateLookup
{
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(5);

    private readonly IProviderCostRateRepository _repository;
    private readonly IMemoryCache _cache;

    public ProviderCostRateLookup(IProviderCostRateRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<ProviderCostRate?> GetRateAsync(
        string providerKey,
        string? model,
        DateTime atUtc,
        CancellationToken cancellationToken)
    {
        // Round atUtc to hour for stable cache keys — rates change at most daily.
        var roundedUtc = new DateTime(atUtc.Year, atUtc.Month, atUtc.Day, atUtc.Hour, 0, 0, DateTimeKind.Utc);
        var cacheKey = $"cost-rate:{providerKey}:{model ?? "__null__"}:{roundedUtc:yyyyMMddHH}";

        if (_cache.TryGetValue(cacheKey, out ProviderCostRate? cached))
        {
            return cached;
        }

        var rate = await _repository.GetEffectiveRateAsync(providerKey, model, atUtc, cancellationToken);

        // Cache the result (including null — no rate found) so we don't hammer the DB.
        var options = new MemoryCacheEntryOptions
        {
            SlidingExpiration = SlidingExpiration
        };
        _cache.Set(cacheKey, rate, options);

        return rate;
    }
}
