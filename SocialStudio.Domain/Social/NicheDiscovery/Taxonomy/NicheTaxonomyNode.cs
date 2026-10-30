using SocialStudio.Domain.Social.Niches;

namespace SocialStudio.Domain.Social.NicheDiscovery.Taxonomy;

/// <summary>
/// Taxonomy hierarchy root for one niche: the flat catalog <see cref="NicheId"/> (cross-referenced
/// by value, never modified) plus the sub-niches discovered under it.
/// </summary>
public sealed record NicheTaxonomyNode(NicheId NicheId, IReadOnlyList<SubNicheRef> SubNiches);
