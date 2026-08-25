namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

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
public sealed record BrandMarkDetection(
    bool Present,
    string? PositionHint = null,
    string? ColorHex = null,
    string? MarkType = null,
    string? Text = null)
{
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
}
