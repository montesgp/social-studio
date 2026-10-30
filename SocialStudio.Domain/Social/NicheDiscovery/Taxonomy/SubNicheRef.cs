using SocialStudio.Domain.Social.Niches;

namespace SocialStudio.Domain.Social.NicheDiscovery.Taxonomy;

/// <summary>
/// One AI-proposed sub-niche label under a niche from the existing flat catalog. Cross-references
/// <see cref="NicheId"/> BY VALUE only — no navigation property, no modification of
/// <see cref="Niche"/> (settled decision 2, design §14).
/// </summary>
public sealed record SubNicheRef(NicheId NicheId, string Label);
