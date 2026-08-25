namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Fonts;

/// <summary>
/// Maps a font family OBSERVED in a client's posts onto a family the renderer can actually embed.
///
/// <para>
/// Fonts are embedded resources (Decision A1 — the render host's font state is unknown, so nothing
/// may depend on host-provisioned fonts), and Arial/Helvetica are Monotype-licensed and must never
/// be aliased to (Decision #1169). A detected family is therefore only renderable when a binary for
/// it is bundled; anything else has to fall back to the closest bundled OFL family with the same
/// classification, so a serif headline never silently becomes a sans one.
/// </para>
///
/// <para>
/// The detected name is never discarded by callers — <see cref="Resolve"/> reports both what the
/// client actually uses and what will be drawn, so the gap is visible instead of silent.
/// </para>
/// </summary>
public static class FontFamilyResolution
{
    private const string BundledSans = "Liberation Sans";
    private const string BundledSerif = "Liberation Serif";
    private const string BundledMono = "Liberation Mono";

    /// <summary>
    /// Families whose names contain these tokens are serif designs. Checked before the sans tokens
    /// because "sans serif" contains "serif" — order matters, see <see cref="Classify"/>.
    /// </summary>
    private static readonly string[] SerifTokens =
    [
        "serif", "georgia", "garamond", "playfair", "merriweather", "lora", "times",
        "baskerville", "didot", "bodoni", "cormorant", "libre caslon", "spectral", "tinos",
    ];

    private static readonly string[] MonoTokens =
    [
        "mono", "courier", "consolas", "menlo", "jetbrains", "source code", "ibm plex mono",
    ];

    /// <summary>
    /// Resolves a detected family to a renderable one.
    /// </summary>
    /// <returns>
    /// <c>Requested</c> is the detected family, trimmed. <c>Renderable</c> is the family the renderer
    /// will draw with. <c>IsExact</c> is true only when the detected family is itself bundled — when
    /// false, the caller is looking at a substitute and should say so rather than claim fidelity.
    /// </returns>
    public static (string Requested, string Renderable, bool IsExact) Resolve(string? detectedFamily)
    {
        var requested = (detectedFamily ?? string.Empty).Trim();
        if (requested.Length == 0)
        {
            return (string.Empty, BundledSans, false);
        }

        // A bundled family resolves to itself — that is the only exact case.
        var bundled = FontCatalog.SystemSafeList
            .FirstOrDefault(f => string.Equals(f, requested, StringComparison.OrdinalIgnoreCase));

        if (bundled is not null)
        {
            return (requested, bundled, true);
        }

        return (requested, Classify(requested), false);
    }

    /// <summary>
    /// Picks the bundled family whose classification matches the detected one. Serif is tested first
    /// because every "sans serif" name also contains "serif"; testing sans first would misclassify
    /// every serif family in the list.
    /// </summary>
    private static string Classify(string family)
    {
        var lower = family.ToLowerInvariant();

        if (MonoTokens.Any(t => lower.Contains(t, StringComparison.Ordinal)))
        {
            return BundledMono;
        }

        // "sans" wins over "serif" when both appear, which is exactly the "sans serif" case.
        if (lower.Contains("sans", StringComparison.Ordinal))
        {
            return BundledSans;
        }

        if (SerifTokens.Any(t => lower.Contains(t, StringComparison.Ordinal)))
        {
            return BundledSerif;
        }

        // Unknown families are overwhelmingly sans in social design; default there rather than
        // guessing serif and changing the whole feel of a deck.
        return BundledSans;
    }
}
