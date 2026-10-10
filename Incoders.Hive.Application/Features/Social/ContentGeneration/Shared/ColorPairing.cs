namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// A ranked background-text colour pairing derived from the client's brand palette + extracted colours.
/// Stored in <see cref="ClientAestheticProfile.ColorPairings"/> and applied per-slide by rotating
/// <c>pairings[(slideIndex-1) % pairings.Count]</c> in the carousel visual composer.
/// WCAG contrast ≥ 4.5:1 is enforced at derivation time (<see cref="ColorPairingDerivation"/>).
/// Pure Application value object — no provider/EF/Infrastructure types.
/// Refs: design §3.1/§3.3 OQ-2; CAC-BSP-03.
/// </summary>
/// <param name="BackgroundHex">Background hex colour (e.g. "#1A2B3C").</param>
/// <param name="TextHex">Text hex colour with WCAG ≥ 4.5:1 contrast against <see cref="BackgroundHex"/>.</param>
/// <param name="Rank">Prevalence rank from the source brand palette; 1 = highest priority.</param>
/// <param name="AccentTextHex">
/// Optional THIRD colour for emphasised copy on the same slide — the "fixed background, body copy in
/// one colour, highlighted words in another" combination real client feeds use (e.g. red background,
/// white body, yellow highlight). Holds WCAG ≥ 4.5:1 against <see cref="BackgroundHex"/> and is never
/// equal to <see cref="TextHex"/>. Null when the palette offers no legible third colour, in which case
/// the slide renders as a plain two-colour pairing exactly as before.
/// </param>
/// <param name="SlideCount">
/// client-style-fidelity (CSF-PA-03): how many of the client's analysed slides use this combination.
/// Present only for combinations OBSERVED in the client's posts; null for pairings derived from the
/// palette's role hierarchy, which keep the legacy rotation.
/// </param>
/// <param name="Kind">
/// <see cref="ColorPairingKinds.Flat"/> (text over a solid background) or
/// <see cref="ColorPairingKinds.Photo"/> (text over a photograph, where <see cref="BackgroundHex"/>
/// is the tone behind the text). Null for derived pairings.
/// </param>
public sealed record ColorPairing(
    string BackgroundHex,
    string TextHex,
    int Rank,
    string? AccentTextHex = null,
    int? SlideCount = null,
    string? Kind = null)
{
    /// <summary>True when the pairing was observed in the client's posts rather than derived.</summary>
    public bool IsObserved => SlideCount is > 0;
}

/// <summary>Values of <see cref="ColorPairing.Kind"/>.</summary>
public static class ColorPairingKinds
{
    public const string Flat = "flat";
    public const string Photo = "photo";
}
