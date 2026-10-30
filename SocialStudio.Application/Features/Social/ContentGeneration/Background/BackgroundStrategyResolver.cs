using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// DI-keyed strategy map: maps a <see cref="BackgroundSourcingPolicy"/> to the correct
/// <see cref="IBackgroundSourcingStrategy"/> implementation.
///
/// CASE D extensibility: register a new IBackgroundSourcingStrategy with a new policy value
/// and it resolves automatically — the A/B/C strategy classes and this resolver are untouched.
/// This is the OCP seam verified by BackgroundStrategyResolverTests (BSP-06).
///
/// Refs: design §4.2; BSP-05/06.
/// </summary>
public sealed class BackgroundStrategyResolver : IBackgroundStrategyResolver
{
    private readonly IReadOnlyDictionary<BackgroundSourcingPolicy, IBackgroundSourcingStrategy> _map;
    private readonly IBackgroundSourcingStrategy _legacyFallback;

    public BackgroundStrategyResolver(
        IEnumerable<IBackgroundSourcingStrategy> strategies,
        IBackgroundSourcingStrategy legacyFallback)
    {
        _legacyFallback = legacyFallback;

        // Build keyed map from all registered strategies.
        // Last registration wins for duplicate policy keys (DI convention).
        var dict = new Dictionary<BackgroundSourcingPolicy, IBackgroundSourcingStrategy>();
        foreach (var strategy in strategies)
        {
            dict[strategy.Policy] = strategy;
        }
        _map = dict;
    }

    /// <inheritdoc />
    public IBackgroundSourcingStrategy Resolve(BackgroundSourcingPolicy? policy)
    {
        // null / Unset / unknown → legacy 3-tier behavior (backward-compatible default)
        if (policy is null || policy == BackgroundSourcingPolicy.Unset)
        {
            return _legacyFallback;
        }

        return _map.TryGetValue(policy.Value, out var strategy)
            ? strategy
            : _legacyFallback;
    }
}
