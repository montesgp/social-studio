namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// client-style-fidelity CSF-PA-04: keeps only the colour pairings whose background demonstrably
/// appears in the client's own posts.
///
/// <para>
/// A pairing derived from the palette's role hierarchy, or reported by a vision model, can name a
/// background the client never uses — the accent green of Demo Roasters became a full lime slide
/// with black text, a treatment that exists nowhere in their feed. A solid background is a large,
/// uniform area, so it is cheap to verify: if no reference image is covered by that colour to at
/// least <see cref="MinimumCoverage"/>, the pairing is invented and is dropped.
/// </para>
///
/// <para>
/// Photo pairings are exempt: their background is a photograph, and their <c>BackgroundHex</c> is
/// only the tone behind the text.
/// </para>
/// </summary>
public static class ColorPairingEvidence
{
    /// <summary>
    /// Share of one reference image a flat background must cover to count as used. A full-bleed flat
    /// slide covers far more than this even with a product shot or headline on top of it.
    /// </summary>
    public const double MinimumCoverage = 0.30;

    /// <param name="pairings">Pairings to verify, in priority order (kept order is preserved).</param>
    /// <param name="maxCoverage">
    /// Largest share (0..1) of any single reference image covered by a colour close to the given hex.
    /// </param>
    public static IReadOnlyList<ColorPairing> Ground(
        IReadOnlyList<ColorPairing> pairings,
        Func<string, double> maxCoverage)
    {
        ArgumentNullException.ThrowIfNull(pairings);
        ArgumentNullException.ThrowIfNull(maxCoverage);

        return pairings
            .Where(p => string.Equals(p.Kind, ColorPairingKinds.Photo, StringComparison.OrdinalIgnoreCase)
                        || maxCoverage(p.BackgroundHex) >= MinimumCoverage)
            .ToArray();
    }
}
