namespace Incoders.Hive.Application.Abstractions.Observability;

/// <summary>
/// Reads effective-dated provider cost rates.
/// </summary>
public interface IProviderCostRateRepository
{
    /// <summary>
    /// Returns the cost rate effective at <paramref name="atUtc"/> for the given
    /// <paramref name="providerKey"/> and optional <paramref name="model"/>.
    /// Returns <c>null</c> when no matching rate exists.
    /// </summary>
    Task<ProviderCostRate?> GetEffectiveRateAsync(
        string providerKey,
        string? model,
        DateTime atUtc,
        CancellationToken cancellationToken);
}

/// <summary>Read model returned by <see cref="IProviderCostRateRepository"/>.</summary>
public sealed record ProviderCostRate(
    string ProviderKey,
    string? Model,
    decimal? CostPer1KInputTokens,
    decimal? CostPer1KOutputTokens,
    decimal? CostPerRequest,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo);
