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
public sealed record ColorPairing(
    string BackgroundHex,
    string TextHex,
    int Rank,
    string? AccentTextHex = null);
