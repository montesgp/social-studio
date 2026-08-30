namespace Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// One discovery provider in the ordered <c>ContentDiscoveryChain</c>. Mirrors
/// <c>IContentIdeaSource</c> in shape.
/// </summary>
public interface IContentDiscoverySource
{
    /// <summary>Stable key used in shortfall/failed/unconfigured reporting.</summary>
    string SourceKey { get; }

    /// <summary>Preference order. DI registration order never decides precedence.</summary>
    int Order { get; }

    bool Supports(string searchCriterion);

    Task<SourceDiscoveryResult> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct = default);
}
