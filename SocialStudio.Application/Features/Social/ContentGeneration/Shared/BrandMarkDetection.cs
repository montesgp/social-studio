namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// A recurring identifying mark (logo/emblem/watermark) the multimodal extractor detected across
/// a client's analyzed post images. FFS-PA-01; extractor schema v3.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Present"/> is the authoritative signal: when <c>false</c>, no mark was reliably
/// detected and the other fields carry no meaning — callers MUST NOT render a mark. When
/// <c>true</c>, <see cref="PositionHint"/> and <see cref="ColorHex"/> are best-effort, optional
/// signals; either may be <c>null</c> (color-source: <c>null</c> means "use the slide's text
/// color" rather than an explicit hex).
/// </para>
/// <para>
/// An unrecognised/ambiguous <see cref="PositionHint"/> or a malformed <see cref="ColorHex"/> MUST
/// be treated as if the WHOLE mark were absent at render time (FFS-CG-02-c) — the renderer never
/// guesses a fallback position/color for an ambiguous signal, it simply omits the mark.
/// </para>
/// <para>Pure Application value object — no Infrastructure/provider types.</para>
/// <para>
/// WU-6a (CAGH-PA-01/02, schema v5) adds two further additive-nullable fields:
/// <see cref="MarkType"/> and <see cref="Text"/>. Both are defensively parsed — a missing,
/// unrecognised, or malformed value never throws and never fabricates a signal; a profile
/// persisted before this change deserializes with both fields <c>null</c> (FFS-PA-02-c).
/// </para>
/// <para>
/// WU-6b (CAGH-PA-03/04, schema v6) adds <see cref="SourceImagePath"/> and
/// <see cref="BoundingBox"/> — both meaningful only for icon marks, both defensively validated:
/// an unresolvable source index or a malformed/out-of-range bounding box never throws, it simply
/// maps to <c>null</c> (FFS-PA-02-c convention extended).
/// </para>
/// </remarks>
/// <param name="Present">True when a recurring mark was reliably detected across the client's corpus.</param>
/// <param name="PositionHint">Approximate position (e.g. "bottom-right"); null when not signaled.</param>
/// <param name="ColorHex">Explicit mark color; null means "use the slide's text color" (color-source).</param>
/// <param name="MarkType">
/// Discriminator: <c>"text"</c>, <c>"icon"</c>, or <c>"unknown"</c>. Any extractor value that does
/// not case-insensitively match <c>"text"</c>/<c>"icon"</c> maps to <c>"unknown"</c>; null on
/// profiles persisted before this field existed.
/// </param>
/// <param name="Text">
/// The literal signature text, meaningful only when <see cref="MarkType"/> is <c>"text"</c>;
/// null for icon/unknown marks or when the extractor did not return one.
/// </param>
/// <param name="SourceImagePath">
/// The archived <c>ProfilePostImageRef.StoragePath</c> the mark was seen on, resolved by the
/// extractor from its own post-skip <c>loadedPairs</c> index (D9, CAGH-PA-03); null when the
/// index is missing, out of range, or unresolvable, or when <see cref="MarkType"/> is not
/// <c>"icon"</c>.
/// </param>
/// <param name="BoundingBox">
/// Normalised 0..1 crop region within <see cref="SourceImagePath"/> (CAGH-PA-04); null when the
/// extractor did not return one, or the returned box is malformed, out of range, or below the
/// minimum-area guard. Invalid boxes never reach this field — validation happens before mapping.
/// </param>
public sealed record BrandMarkDetection(
    bool Present,
    string? PositionHint = null,
    string? ColorHex = null,
    string? MarkType = null,
    string? Text = null,
    string? SourceImagePath = null,
    BrandMarkBoundingBox? BoundingBox = null)
{
    /// <summary>Minimum normalised area (3% × 3%) a bounding box must clear to be accepted (CAGH-PA-04).</summary>
    public const double MinimumNormalizedArea = 0.0009;

    /// <summary>
    /// Normalises a raw extractor <c>markType</c> token to <c>"text"</c>/<c>"icon"</c>/<c>"unknown"</c>
    /// (case-insensitive; CAGH-PA-01). Null, blank, or any unrecognised token maps to <c>"unknown"</c>.
    /// Never throws — pure defensive parsing, per the FFS-PA-02 convention.
    /// </summary>
    public static string NormalizeMarkType(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "text" => "text",
            "icon" => "icon",
            _ => "unknown",
        };

    /// <summary>
    /// Validates and normalises a raw bounding box (CAGH-PA-04). Returns null — never throws — when
    /// any coordinate is missing/non-finite, out of the <c>[0,1]</c> range, the box is inverted
    /// (<c>Width</c>/<c>Height</c> &lt;= 0), <c>X+Width</c> or <c>Y+Height</c> exceeds <c>1.0</c>, or
    /// the normalised area is below <see cref="MinimumNormalizedArea"/>.
    /// </summary>
    public static BrandMarkBoundingBox? TryValidateBoundingBox(double? x, double? y, double? width, double? height)
    {
        if (x is not { } px || y is not { } py || width is not { } pw || height is not { } ph)
        {
            return null;
        }

        if (!double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(pw) || !double.IsFinite(ph))
        {
            return null;
        }

        if (px < 0 || px > 1 || py < 0 || py > 1)
        {
            return null;
        }

        if (pw <= 0 || ph <= 0)
        {
            return null;
        }

        if (px + pw > 1.0 || py + ph > 1.0)
        {
            return null;
        }

        var area = pw * ph;
        if (area < MinimumNormalizedArea)
        {
            return null;
        }

        return new BrandMarkBoundingBox(px, py, pw, ph);
    }
}

/// <summary>
/// Normalised <c>0..1</c> crop region (origin top-left) within a <see cref="BrandMarkDetection.SourceImagePath"/>
/// reference image (CAGH-PA-04). Invalid boxes never reach this record — the extractor maps
/// out-of-range, non-finite, inverted, or below-minimum-area values to null before construction.
/// </summary>
/// <param name="X">Left edge, normalised 0..1.</param>
/// <param name="Y">Top edge, normalised 0..1.</param>
/// <param name="Width">Box width, normalised 0..1.</param>
/// <param name="Height">Box height, normalised 0..1.</param>
public sealed record BrandMarkBoundingBox(double X, double Y, double Width, double Height);
