namespace SocialStudio.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// Configuration-bound base address for the YouTube Data API v3 origin (design §5.2). Bound from
/// <c>IOptions&lt;NicheDiscoveryOriginOptions&gt;</c> — never a code literal. A null/blank
/// <see cref="BaseAddress"/> degrades the origin to <c>Unconfigured</c>, never a throw.
/// </summary>
public sealed class NicheDiscoveryOriginOptions
{
    public const string SectionName = "NicheDiscovery:YouTubeDataApi";

    public string? BaseAddress { get; set; }

    /// <summary>
    /// ISO 3166 regions the query-less sweep samples from, in order. Configuration rather than a run
    /// parameter ON PURPOSE: the provider's only query-less feed is region-addressed, so this is a
    /// sourcing knob — nothing downstream partitions, scores or compares by region, and no region
    /// value reaches the domain model.
    ///
    /// Empty or unset means no sweep is possible, which surfaces as a run that sourced nothing rather
    /// than a guess about which part of the world to sample.
    /// </summary>
    public IReadOnlyList<string> SamplingRegions { get; set; } = [];

    /// <summary>
    /// Caps how many categories the query-less sweep walks PER REGION. Unlike the opportunity ceiling,
    /// this one genuinely saves quota: every category skipped is one provider call not made.
    ///
    /// <c>null</c> or non-positive means walk the whole taxonomy. Set it low while developing — a full
    /// sweep of four regions costs roughly one call per category per region, and a broad corpus is worth
    /// nothing when you are only checking that the wiring holds.
    /// </summary>
    public int? MaxCategoriesPerRegion { get; set; }
}
