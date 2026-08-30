namespace Incoders.Hive.Domain.Social.NicheDiscovery;

/// <summary>
/// The per-stage routing axis: each stage is resolved to an origin independently
/// (design §5.1/§5.2), so a provider strong at search but weak at detail enrichment can be mixed
/// with another origin.
/// </summary>
public enum DiscoveryStage
{
    ChannelSearch = 1,
    VideoSearch = 2,
    ChannelDetail = 3,
    VideoDetail = 4,

    /// <summary>
    /// Query-less discovery: harvests what is already performing, without the operator naming it
    /// first. The search stages need a term, which makes them "confirm this niche" rather than "find
    /// one" — this stage is what makes the feature deserve the name discovery.
    ///
    /// APPENDED, never renumbered: the value is persisted in run snapshots.
    /// </summary>
    TrendingSweep = 5,
}
