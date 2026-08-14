namespace Incoders.Hive.Domain.Social.NicheDiscovery.Taxonomy;

/// <summary>
/// Closed enum of recognized content patterns (design §8, N4 — a label crossing the AI boundary is
/// only ever a closed enum). <see cref="Unclassified"/> is the semantic-failure fallback: an unknown
/// AI-proposed pattern label, or the lexical fallback clustering path, always maps here rather than
/// being coerced into an arbitrary pattern (N11).
/// </summary>
public enum ContentPatternKind
{
    Unclassified = 0,
    Tutorial = 1,
    Listicle = 2,
    Review = 3,
    Comparison = 4,
    Storytelling = 5,
    BehindTheScenes = 6,
    Challenge = 7,
    NewsCommentary = 8,
    Compilation = 9,
}
