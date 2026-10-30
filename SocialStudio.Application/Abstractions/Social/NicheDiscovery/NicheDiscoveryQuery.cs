using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// One stage's worth of sourcing parameters handed to an <see cref="INicheDiscoverySource"/>.
/// <see cref="ChannelExternalRefs"/>/<see cref="VideoExternalRefs"/> are populated for the detail
/// stages (<see cref="DiscoveryStage.ChannelDetail"/>/<see cref="DiscoveryStage.VideoDetail"/>), empty
/// otherwise.
/// </summary>
public sealed record NicheDiscoveryQuery(
    Guid OrganizationId,
    DiscoveryStage Stage,
    string NetworkKey,
    string ContentFormat,
    /// <summary>The target market's language (design §4) — never a market identity.</summary>
    string TargetMarketLanguage,
    DiscoveryWindow Window,
    IReadOnlyList<string> SeedTerms,
    IReadOnlyList<string> ChannelExternalRefs,
    IReadOnlyList<string> VideoExternalRefs);
