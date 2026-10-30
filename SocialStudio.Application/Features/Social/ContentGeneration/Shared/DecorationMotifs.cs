namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// The recurring divider-rule style detected across a client's analyzed post images (CAVS-PA-03).
/// Closed enum: its value set is bounded by what the deterministic CSS divider primitive can draw,
/// not by client diversity — unlike <see cref="CompositionArchetypes"/>, no future value needs
/// adding here without a matching render primitive.
/// </summary>
public enum DividerRuleStyle
{
    /// <summary>A plain horizontal/vertical rule, no glyph.</summary>
    Line = 1,

    /// <summary>A rule with a nested rotated-square (diamond) glyph.</summary>
    LineWithDiamond = 2,

    /// <summary>A rule with a nested dot glyph.</summary>
    LineWithDot = 3,
}

/// <summary>
/// A recurring decorative ornament (non-brand-mark) the multimodal extractor detected across a
/// client's analyzed post images (CAVS-PA-04). Reuses <see cref="BrandMarkBoundingBox"/> verbatim —
/// same normalised 0..1 crop-region shape and the same <see cref="BrandMarkDetection.TryValidateBoundingBox"/>
/// / <see cref="BrandMarkDetection.MinimumNormalizedArea"/> validation guard as brand-mark icons.
/// </summary>
/// <param name="SourceImagePath">
/// The archived <c>ProfilePostImageRef.StoragePath</c> the ornament was seen on, resolved by the
/// extractor from its own post-skip <c>loadedPairs</c> index (D9, CAGH-PA-03 precedent); null when
/// the index is missing, out of range, or unresolvable.
/// </param>
/// <param name="BoundingBox">
/// Normalised 0..1 crop region within <see cref="SourceImagePath"/>; null when the extractor did not
/// return one, or the returned box is malformed, out of range, or below the minimum-area guard.
/// Invalid boxes never reach this field — validation happens before mapping (CAVS-PA-04-b).
/// </param>
public sealed record DecorativeOrnamentDetection(
    string? SourceImagePath = null,
    BrandMarkBoundingBox? BoundingBox = null);

/// <summary>
/// The client's recurring decoration signals: a divider-rule style, an open composition-archetype
/// classification token, and a nested decorative ornament detection (CAVS-PA-01). One additive-
/// nullable field on <see cref="SocialStudio.Application.Abstractions.Social.ContentGeneration.IClientAestheticStyleExtractor"/>'s
/// extraction result and on <c>ClientAestheticProfile</c>; a profile persisted before this change
/// deserializes with <c>DecorationMotifs = null</c>, throwing nothing (CAVS-PA-01-b).
/// </summary>
/// <param name="DividerRule">
/// The detected divider-rule style; <c>null</c> means unmeasured/absent — no companion boolean flag
/// (CAVS-PA-03-b: an unrecognised token also maps here as <c>null</c>).
/// </param>
/// <param name="CompositionArchetype">
/// Open, normalised classification token (trim + lowercase only); an unrecognised token is preserved
/// verbatim, never collapsed to a placeholder (CAVS-PA-02-b) — see <see cref="CompositionArchetypes.Normalize"/>.
/// </param>
/// <param name="Ornament">The nested decorative ornament detection; null when none was reliably detected.</param>
public sealed record DecorationMotifs(
    DividerRuleStyle? DividerRule = null,
    string? CompositionArchetype = null,
    DecorativeOrnamentDetection? Ornament = null);

/// <summary>
/// Recognised <see cref="DecorationMotifs.CompositionArchetype"/> tokens and the defensive
/// normalisation/parsing helpers around them (CAVS-PA-02, CAVS-PA-03).
/// </summary>
/// <remarks>
/// <see cref="Normalize"/> is deliberately the ONLY mutation a raw archetype token ever undergoes,
/// and unlike <see cref="BrandMarkDetection.NormalizeMarkType"/>, it NEVER collapses an unrecognised
/// token to a placeholder such as <c>"unknown"</c> — it preserves the trimmed/lowercased token
/// verbatim, so recognising a new archetype later is a code-only change (one new const plus one new
/// render preset), never a schema or data migration (design D1).
/// </remarks>
public static class CompositionArchetypes
{
    /// <summary>Identity preset — reproduces today's hardcoded flat-carousel geometry exactly.</summary>
    public const string LeftAligned = "left-aligned";

    public const string Centered = "centered";

    public const string LowerThird = "lower-third";

    /// <summary>
    /// Trims and lowercases a raw archetype token; null/blank maps to <c>null</c>. Never collapses
    /// an unrecognised token — the caller gets the normalised token back verbatim (CAVS-PA-02-b).
    /// </summary>
    public static string? Normalize(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();

    /// <summary>
    /// True when the normalised token matches one of the recognised v1 archetypes
    /// (<see cref="LeftAligned"/>, <see cref="Centered"/>, <see cref="LowerThird"/>).
    /// </summary>
    public static bool IsRecognised(string? raw) =>
        Normalize(raw) is LeftAligned or Centered or LowerThird;

    /// <summary>
    /// Parses a raw divider-rule token (case-insensitive) to its <see cref="DividerRuleStyle"/>.
    /// An unrecognised, missing, or malformed token maps to <c>null</c>, never throws (CAVS-PA-03-b).
    /// </summary>
    public static DividerRuleStyle? TryParseDividerRule(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "line" => DividerRuleStyle.Line,
            "line-with-diamond" => DividerRuleStyle.LineWithDiamond,
            "line-with-dot" => DividerRuleStyle.LineWithDot,
            _ => null,
        };
}
