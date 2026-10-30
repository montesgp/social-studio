using SocialStudio.Domain.Social.NicheDiscovery.Taxonomy;

namespace SocialStudio.Application.Features.Social.NicheDiscovery.Semantics;

/// <summary>
/// <c>PatternLabel</c> (the AI's free-text proposal, or the lexical fallback's constant) → the closed
/// <see cref="ContentPatternKind"/> enum (design §8, N4). Unknown or unparseable labels — including
/// every label <c>LexicalClusterFallback</c> ever produces — map to
/// <see cref="ContentPatternKind.Unclassified"/>, never guessed into an arbitrary pattern (N11). A
/// static utility, not a <c>…Semantics</c> type itself — it has no properties, so guard (b) does not
/// apply to it (design §3 guard 2 constrains PROPERTIES of Semantics TYPES, not utility methods).
/// </summary>
public static class ContentPatternKindMapper
{
    public static ContentPatternKind FromLabel(string? label) =>
        !string.IsNullOrWhiteSpace(label) && Enum.TryParse<ContentPatternKind>(label.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : ContentPatternKind.Unclassified;
}
