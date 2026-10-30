using SocialStudio.Domain.Social.ClientBrandManuals;

namespace SocialStudio.Domain.Social.ContentGeneration;

/// <summary>
/// Snapshot mirror of a brand color entry, carrying structured role and rank information.
/// Lives in ContentGeneration (not ClientBrandManuals) alongside other snapshot types
/// so the snapshot contract does not depend on the ClientBrandManuals aggregate.
/// Pre-sorted by prevalence at snapshot load time: (Role ordinal, Rank).
/// </summary>
public sealed record WeightedColor(string Hex, ColorRole? Role, int Rank);
