namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// Port for resolving embedded (bundled) font bytes and probing render-side font capabilities.
/// </summary>
/// <remarks>
/// Implementations MUST return raw byte streams and metadata only — no rendering-engine-specific
/// types (e.g. SkiaSharp's <c>SKTypeface</c>) may appear on this surface. Infrastructure owns any
/// engine-specific typeface cache; Application never depends on it
/// (see <c>architecture-boundaries.rules.md</c> — Infrastructure implements adapters, never the
/// other way around).
/// <para>
/// Decision A1 (Content Studio editing completeness design): fonts ship as embedded resources and
/// are never assumed present on the render host, because the render host's font state is unknown
/// (no Dockerfile provisions fonts in this repository).
/// </para>
/// </remarks>
public interface IFontProvider
{
    /// <summary>
    /// Resolves the raw font bytes bundled under <paramref name="family"/>, or <c>null</c> when the
    /// family is not embedded. The caller decides how to parse/interpret the bytes.
    /// </summary>
    byte[]? Resolve(string family);

    /// <summary>
    /// Every embedded face of <paramref name="family"/> with the weight range it covers, so a
    /// renderer can declare real weights instead of letting the engine synthesise bold. Empty when
    /// the family is not embedded. The default treats <see cref="Resolve"/>'s single file as a
    /// regular-weight face, which is what every provider returned before the catalog existed.
    /// </summary>
    IReadOnlyList<EmbeddedFontFace> ResolveFaces(string family)
    {
        var bytes = Resolve(family);
        return bytes is { Length: > 0 } ? [new EmbeddedFontFace(bytes, 400, 400)] : [];
    }

    /// <summary>
    /// Reports which render-side capabilities are actually available, computed by probing the
    /// bundled typefaces for known codepoints — never a static configuration flag, because a flag
    /// can lie about what the render path can actually draw (Decision F3).
    /// </summary>
    FontCapability Probe();
}
