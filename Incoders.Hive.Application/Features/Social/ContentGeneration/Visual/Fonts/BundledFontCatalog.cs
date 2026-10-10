namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>Broad design class of a bundled family, used to map a described font to the closest one.</summary>
public enum FontClassification
{
    Display,
    Sans,
    Serif,
    Mono,
    Script,
}

/// <summary>Horizontal proportion of a bundled family.</summary>
public enum FontWidth
{
    Condensed,
    Normal,
    Wide,
}

/// <summary>
/// One embedded font file. A variable font covers <see cref="MinWeight"/>..<see cref="MaxWeight"/>;
/// a static file has both set to its single weight.
/// </summary>
public sealed record BundledFontFace(string File, int MinWeight, int MaxWeight);

/// <summary>
/// A font family embedded in the render host (client-style-fidelity D1). <see cref="Directory"/> is
/// the asset folder under <c>Rendering/Fonts/Assets/Catalog</c> that holds its files and OFL licence.
/// </summary>
public sealed record BundledFontFamily(
    string Name,
    string Directory,
    FontClassification Classification,
    FontWidth Width,
    IReadOnlyList<BundledFontFace> Faces)
{
    /// <summary>
    /// The nearest weight this family actually ships. Asking Chromium for a weight the face does not
    /// have makes it synthesise a fake bold, which is exactly how a condensed client headline turned
    /// into a wide, heavy one.
    /// </summary>
    public int ClampWeight(int weight)
    {
        var best = weight;
        var bestDistance = int.MaxValue;
        foreach (var face in Faces)
        {
            var candidate = Math.Clamp(weight, face.MinWeight, face.MaxWeight);
            var distance = Math.Abs(candidate - weight);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }
}

/// <summary>
/// The curated OFL families bundled for client typography (client-style-fidelity D1). Sourced from
/// the <c>ofl/</c> tree of github.com/google/fonts; weight ranges are read from each file's
/// <c>fvar</c>/<c>OS/2</c> tables, not assumed.
/// </summary>
public static class BundledFontCatalog
{
    public static readonly IReadOnlyList<BundledFontFamily> Families =
    [
        new("League Gothic", "leaguegothic", FontClassification.Display, FontWidth.Condensed, [new BundledFontFace("LeagueGothic[wdth].ttf", 400, 400)]),
        new("Antonio", "antonio", FontClassification.Display, FontWidth.Condensed, [new BundledFontFace("Antonio[wght].ttf", 100, 700)]),
        new("Anton", "anton", FontClassification.Display, FontWidth.Condensed, [new BundledFontFace("Anton-Regular.ttf", 400, 400)]),
        new("Oswald", "oswald", FontClassification.Display, FontWidth.Condensed, [new BundledFontFace("Oswald[wght].ttf", 200, 700)]),
        new("Bebas Neue", "bebasneue", FontClassification.Display, FontWidth.Condensed, [new BundledFontFace("BebasNeue-Regular.ttf", 400, 400)]),
        new("Fjalla One", "fjallaone", FontClassification.Display, FontWidth.Condensed, [new BundledFontFace("FjallaOne-Regular.ttf", 400, 400)]),
        new("Barlow Condensed", "barlowcondensed", FontClassification.Sans, FontWidth.Condensed, [new BundledFontFace("BarlowCondensed-Regular.ttf", 400, 400), new BundledFontFace("BarlowCondensed-Medium.ttf", 500, 500), new BundledFontFace("BarlowCondensed-SemiBold.ttf", 600, 600), new BundledFontFace("BarlowCondensed-Bold.ttf", 700, 700), new BundledFontFace("BarlowCondensed-ExtraBold.ttf", 800, 800)]),
        new("Roboto Condensed", "robotocondensed", FontClassification.Sans, FontWidth.Condensed, [new BundledFontFace("RobotoCondensed[wght].ttf", 100, 900)]),
        new("Sofia Sans Extra Condensed", "sofiasansextracondensed", FontClassification.Display, FontWidth.Condensed, [new BundledFontFace("SofiaSansExtraCondensed[wght].ttf", 1, 1000)]),
        new("Archivo Narrow", "archivonarrow", FontClassification.Sans, FontWidth.Condensed, [new BundledFontFace("ArchivoNarrow[wght].ttf", 400, 700)]),
        new("Teko", "teko", FontClassification.Display, FontWidth.Condensed, [new BundledFontFace("Teko[wght].ttf", 300, 700)]),
        new("Poppins", "poppins", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("Poppins-Light.ttf", 300, 300), new BundledFontFace("Poppins-Regular.ttf", 400, 400), new BundledFontFace("Poppins-Medium.ttf", 500, 500), new BundledFontFace("Poppins-SemiBold.ttf", 600, 600), new BundledFontFace("Poppins-Bold.ttf", 700, 700), new BundledFontFace("Poppins-ExtraBold.ttf", 800, 800), new BundledFontFace("Poppins-Black.ttf", 900, 900)]),
        new("Montserrat", "montserrat", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("Montserrat[wght].ttf", 100, 900)]),
        new("Inter", "inter", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("Inter[opsz,wght].ttf", 100, 900)]),
        new("DM Sans", "dmsans", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("DMSans[opsz,wght].ttf", 100, 1000)]),
        new("Work Sans", "worksans", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("WorkSans[wght].ttf", 100, 900)]),
        new("Raleway", "raleway", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("Raleway[wght].ttf", 100, 900)]),
        new("Open Sans", "opensans", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("OpenSans[wdth,wght].ttf", 300, 800)]),
        new("Roboto", "roboto", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("Roboto[wdth,wght].ttf", 100, 900)]),
        new("Space Grotesk", "spacegrotesk", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("SpaceGrotesk[wght].ttf", 300, 700)]),
        new("Archivo Black", "archivoblack", FontClassification.Sans, FontWidth.Wide, [new BundledFontFace("ArchivoBlack-Regular.ttf", 400, 400)]),
        new("Nunito", "nunito", FontClassification.Sans, FontWidth.Normal, [new BundledFontFace("Nunito[wght].ttf", 200, 1000)]),
        new("Playfair Display", "playfairdisplay", FontClassification.Serif, FontWidth.Normal, [new BundledFontFace("PlayfairDisplay[wght].ttf", 400, 900)]),
        new("Lora", "lora", FontClassification.Serif, FontWidth.Normal, [new BundledFontFace("Lora[wght].ttf", 400, 700)]),
        new("DM Serif Display", "dmserifdisplay", FontClassification.Serif, FontWidth.Normal, [new BundledFontFace("DMSerifDisplay-Regular.ttf", 400, 400)]),
        new("EB Garamond", "ebgaramond", FontClassification.Serif, FontWidth.Normal, [new BundledFontFace("EBGaramond[wght].ttf", 400, 800)]),
        new("Space Mono", "spacemono", FontClassification.Mono, FontWidth.Normal, [new BundledFontFace("SpaceMono-Regular.ttf", 400, 400), new BundledFontFace("SpaceMono-Bold.ttf", 700, 700)]),
        new("Courier Prime", "courierprime", FontClassification.Mono, FontWidth.Normal, [new BundledFontFace("CourierPrime-Regular.ttf", 400, 400), new BundledFontFace("CourierPrime-Bold.ttf", 700, 700)]),
        new("JetBrains Mono", "jetbrainsmono", FontClassification.Mono, FontWidth.Normal, [new BundledFontFace("JetBrainsMono[wght].ttf", 100, 800)]),
        new("IBM Plex Mono", "ibmplexmono", FontClassification.Mono, FontWidth.Normal, [new BundledFontFace("IBMPlexMono-Light.ttf", 300, 300), new BundledFontFace("IBMPlexMono-Regular.ttf", 400, 400), new BundledFontFace("IBMPlexMono-Medium.ttf", 500, 500), new BundledFontFace("IBMPlexMono-SemiBold.ttf", 600, 600), new BundledFontFace("IBMPlexMono-Bold.ttf", 700, 700)]),
        new("Caveat", "caveat", FontClassification.Script, FontWidth.Normal, [new BundledFontFace("Caveat[wght].ttf", 400, 700)]),
        new("Dancing Script", "dancingscript", FontClassification.Script, FontWidth.Normal, [new BundledFontFace("DancingScript[wght].ttf", 400, 700)]),
        new("Pacifico", "pacifico", FontClassification.Script, FontWidth.Normal, [new BundledFontFace("Pacifico-Regular.ttf", 400, 400)]),
    ];

    private static readonly Dictionary<string, BundledFontFamily> ByName =
        Families.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds a bundled family by exact (case-insensitive) name.</summary>
    public static BundledFontFamily? Find(string? family) =>
        family is not null && ByName.TryGetValue(family.Trim(), out var match) ? match : null;

    /// <summary>
    /// Clamps <paramref name="weight"/> to what <paramref name="family"/> ships. Families outside the
    /// catalog (the legacy Liberation/DejaVu fallbacks, brand-manual names) keep the requested weight.
    /// </summary>
    public static int ClampWeight(string? family, int weight) =>
        Find(family)?.ClampWeight(weight) ?? weight;
}
