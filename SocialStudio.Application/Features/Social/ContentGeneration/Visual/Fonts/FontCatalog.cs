namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// Single source of truth for which font families the Content Studio may offer, validate, and
/// render. Consumed by three places that must never disagree (Decision F2 — Content Studio editing
/// completeness design): the guardrail allow-list (<see cref="Visual.VisualGuardrailValidator"/>),
/// the picker UI (via the render-capability endpoint), and the embedded render fonts
/// (<see cref="IFontProvider"/>).
/// </summary>
public static class FontCatalog
{
    /// <summary>
    /// The legacy fallback families (Decision Q5 + F1). Real OFL substitute names — never aliased to
    /// Arial/Helvetica, which are Monotype-licensed and cannot be legally embedded (Decision #1169).
    /// </summary>
    private static readonly string[] FallbackFamilies =
    [
        "Liberation Sans",
        "Liberation Serif",
        "Liberation Mono",
        "DejaVu Sans",
        "DejaVu Serif",
    ];

    /// <summary>
    /// Every family the render host bundles: the fallback families plus the curated client
    /// typography catalog (<see cref="BundledFontCatalog"/>, client-style-fidelity D1). Every entry
    /// here MUST be resolvable by <see cref="IFontProvider"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> SystemSafeList =
        [.. FallbackFamilies, .. BundledFontCatalog.Families.Select(f => f.Name)];

    /// <summary>
    /// Builds the font catalog for a client: brand fonts first (in manual order), then the system
    /// safe list. Blank/whitespace brand entries are skipped. A brand font that happens to share a
    /// name with a system-safe-list font is kept only once, as a brand entry (the picker must not
    /// show the same family twice).
    /// </summary>
    public static IReadOnlyList<FontEntry> For(IReadOnlyList<string> brandTypography)
    {
        var brandFamilies = (brandTypography ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .ToArray();

        var brandSet = new HashSet<string>(brandFamilies, StringComparer.OrdinalIgnoreCase);

        var entries = new List<FontEntry>(brandFamilies.Length + SystemSafeList.Count);
        entries.AddRange(brandFamilies.Select(f => new FontEntry(f, IsBrand: true, IsBundled: false)));
        entries.AddRange(SystemSafeList
            .Where(f => !brandSet.Contains(f))
            .Select(f => new FontEntry(f, IsBrand: false, IsBundled: true)));

        return entries;
    }
}
