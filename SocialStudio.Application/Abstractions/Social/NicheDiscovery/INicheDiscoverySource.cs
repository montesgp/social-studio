using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// One origin in the ordered, per-stage <c>NicheDiscoverySourceChain</c> (design §5.1, N1). A
/// per-provider port (e.g. <c>IYouTubeChannelSearchResolver</c>) is deliberately rejected — origins are
/// adapters selected by configuration, never baked into the Application port shape.
/// </summary>
public interface INicheDiscoverySource
{
    /// <summary>A PROVIDER key (e.g. "youtube-data-api", "apify"), never an actor/endpoint id.</summary>
    string SourceKey { get; }

    /// <summary>Preference order. DI registration order never decides precedence.</summary>
    int Order { get; }

    bool Supports(DiscoveryStage stage);

    Task<NicheDiscoverySourceResult> DiscoverAsync(
        NicheDiscoveryQuery query, DiscoveryBudget budget, CancellationToken ct = default);
}
