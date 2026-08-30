namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// One typographic role observed in the client's real posts by the multimodal aesthetic extractor.
///
/// <para>
/// Before this existed, <c>CompositionDefaults.ResolveTypography</c> took the FIRST brand typography
/// entry, used it for headings AND body alike, and hardcoded the sizes to 28/16 px — so every client
/// rendered at the same two sizes in the same single family regardless of what their feed actually
/// looked like. A client whose feed pairs a heavy sans headline with an italic serif accent had no
/// way to express that.
/// </para>
///
/// <para>
/// <see cref="SizeRatio"/> is deliberately a PROPORTION of canvas height rather than a pixel size:
/// the same profile has to drive a 1080x1350 feed carousel and a 1080x1920 story without the copy
/// changing apparent weight, and a ratio survives both.
/// </para>
/// </summary>
/// <param name="Role">
/// Which slot this describes: <c>heading</c>, <c>body</c>, or <c>accent</c> (the emphasised run
/// inside body copy). Parsed case-insensitively; unrecognised roles are ignored by the resolver.
/// </param>
/// <param name="FontFamily">
/// The family observed in the client's posts, as named by the extractor (e.g. "Poppins"). Kept
/// verbatim even when the renderer cannot embed it, so a later bundled binary can light it up
/// without re-running extraction.
/// </param>
/// <param name="Weight">CSS numeric weight, 100-900. Defaults to 400 when the extractor is unsure.</param>
/// <param name="Italic">True when the role is consistently italic in the client's posts.</param>
/// <param name="SizeRatio">
/// Observed type size as a fraction of canvas height (e.g. 0.075 = 81 px on a 1080x1080 canvas).
/// Clamped by the resolver; values outside a sane range are treated as unusable.
/// </param>
public sealed record AestheticTypography(
    string Role,
    string FontFamily,
    int Weight = 400,
    bool Italic = false,
    double SizeRatio = 0);

/// <summary>The typographic slots a <see cref="AestheticTypography.Role"/> may address.</summary>
public static class TypographyRoles
{
    public const string Heading = "heading";
    public const string Body = "body";
    public const string Accent = "accent";

    /// <summary>Small emphasised copy — a CTA or signature line (D4). Resolved with an <see cref="Overline"/> alias fallback.</summary>
    public const string Caption = "caption";

    /// <summary>Synonym some extractions use for <see cref="Caption"/>.</summary>
    public const string Overline = "overline";
}
