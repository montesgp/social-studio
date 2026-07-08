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
/// </remarks>
/// <param name="Present">True when a recurring mark was reliably detected across the client's corpus.</param>
/// <param name="PositionHint">Approximate position (e.g. "bottom-right"); null when not signaled.</param>
/// <param name="ColorHex">Explicit mark color; null means "use the slide's text color" (color-source).</param>
public sealed record BrandMarkDetection(
    bool Present,
    string? PositionHint = null,
    string? ColorHex = null);
