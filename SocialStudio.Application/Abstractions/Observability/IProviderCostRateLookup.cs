namespace SocialStudio.Application.Abstractions.Observability;

/// <summary>
/// Cached lookup for provider cost rates. Used by the ledger to compute
/// <c>estimated_cost_usd</c> at write time without hitting the database on
/// every provider call.
/// </summary>
public interface IProviderCostRateLookup
{
    /// <summary>
    /// Returns the effective cost rate for the given provider and optional model
    /// at <paramref name="atUtc"/>, or <c>null</c> if no matching rate exists.
    /// Implementations cache results to avoid per-call DB queries.
    /// </summary>
    Task<ProviderCostRate?> GetRateAsync(
        string providerKey,
        string? model,
        DateTime atUtc,
        CancellationToken cancellationToken);
}
