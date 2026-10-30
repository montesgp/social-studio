using SocialStudio.Domain.Social.ClientBrandManuals;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Derives an ordered ranked set of <see cref="ColorPairing"/> records from a client's
/// brand-declared color roles and their multimodal-extracted palette entries.
/// <para>
/// Algorithm (OQ-2, design §3.3):
/// 1. Background candidates — extracted palette entries whose <c>Role</c> matches a value in
///    <paramref name="brandColors"/> AND is not <see cref="ColorRole.Neutral"/>.
/// 2. Sort backgrounds by (role ordinal: Primary=0 &lt; Secondary=1 &lt; Accent=2, then by rank).
/// 3. For each background, choose text: highest-WCAG-contrast Neutral-role candidate ≥ 4.5:1
///    first; else luminance black (#000000) or white (#FFFFFF), whichever yields higher contrast.
/// 4. Return <see cref="ColorPairing"/> list ordered by (role ordinal, rank).
/// </para>
/// Pure Application logic — no provider/EF/Infrastructure types.
/// </summary>
public static class ColorPairingDerivation
{
    /// <summary>
    /// Derives color pairings from the supplied brand role filter and extracted palette.
    /// Returns an empty list when <paramref name="extractedPalette"/> is empty or yields no
    /// background candidates; never throws.
    /// </summary>
    /// <param name="brandColors">
    /// Color roles that are considered valid background candidates (from the brand manual or a
    /// default set). Neutral is never used as a background regardless of this list.
    /// </param>
    /// <param name="extractedPalette">
    /// Palette entries produced by the multimodal aesthetic extractor; role strings are parsed
    /// case-insensitively to <see cref="ColorRole"/>. Entries with unrecognised roles are ignored.
    /// </param>
    public static IReadOnlyList<ColorPairing> Derive(
        IReadOnlyList<ColorRole> brandColors,
        IReadOnlyList<AestheticPaletteEntry> extractedPalette)
    {
        if (brandColors.Count == 0 || extractedPalette.Count == 0)
        {
            return [];
        }

        var brandRoleSet = new HashSet<ColorRole>(brandColors);

        // Background candidates: role in brand set AND not Neutral
        var backgrounds = extractedPalette
            .Select(entry => (Entry: entry, Role: TryParseRole(entry.Role)))
            .Where(x => x.Role.HasValue
                        && x.Role.Value != ColorRole.Neutral
                        && brandRoleSet.Contains(x.Role.Value))
            .OrderBy(x => (int)x.Role.GetValueOrDefault())  // role ordinal: Primary=0, Secondary=1, Accent=2
            .ThenBy(x => x.Entry.Rank)
            .ToList();

        if (backgrounds.Count == 0)
        {
            return [];
        }

        // Neutral-role candidates — tried first as text colours
        var neutralCandidates = extractedPalette
            .Where(p => TryParseRole(p.Role) == ColorRole.Neutral)
            .ToList();

        var result = new List<ColorPairing>(backgrounds.Count);

        foreach (var (entry, _) in backgrounds)
        {
            var bgLuminance = RelativeLuminance(entry.Hex);
            var textHex = PickTextColor(bgLuminance, neutralCandidates);
            var accentHex = PickAccentColor(entry.Hex, bgLuminance, textHex, extractedPalette);
            result.Add(new ColorPairing(entry.Hex, textHex, entry.Rank, accentHex));
        }

        return result;
    }

    /// <summary>
    /// Selects the optional THIRD colour used for emphasised copy on the slide.
    /// <para>
    /// Neutral-role entries are excluded outright: a neutral is body-text material, and picking one
    /// here yields a "highlight" that is visually the same colour as the copy it is meant to stand out
    /// from. Candidates are therefore the CHROMATIC entries — Accent role first, then Secondary, then
    /// Primary — that are neither the background nor the body text, and that clear the same
    /// WCAG ≥ 4.5:1 floor as the body text so a highlight is never less readable than its surroundings.
    /// Ties break on palette rank, so a client's more prevalent colour wins.
    /// </para>
    /// <para>
    /// Returns null when nothing qualifies. That is the ordinary two-colour slide, not a failure.
    /// </para>
    /// </summary>
    private static string? PickAccentColor(
        string backgroundHex,
        double bgLuminance,
        string textHex,
        IReadOnlyList<AestheticPaletteEntry> palette)
    {
        return palette
            .Select(entry => (Entry: entry, Role: TryParseRole(entry.Role)))
            .Where(x => x.Role.HasValue && x.Role.Value != ColorRole.Neutral)
            .Where(x => !SameColor(x.Entry.Hex, backgroundHex) && !SameColor(x.Entry.Hex, textHex))
            .Where(x => ContrastRatio(bgLuminance, RelativeLuminance(x.Entry.Hex)) >= 4.5)
            .OrderBy(x => AccentPriority(x.Role!.Value))
            .ThenBy(x => x.Entry.Rank)
            .Select(x => x.Entry.Hex)
            .FirstOrDefault();
    }

    /// <summary>
    /// Emphasis preference, which is the INVERSE of the background preference: the Accent role is the
    /// colour a brand reserves for highlights, so it is tried first here even though it sorts last when
    /// choosing backgrounds.
    /// </summary>
    private static int AccentPriority(ColorRole role) => role switch
    {
        ColorRole.Accent => 0,
        ColorRole.Secondary => 1,
        ColorRole.Primary => 2,
        _ => 3,
    };

    private static bool SameColor(string left, string right) =>
        string.Equals(left.TrimStart('#'), right.TrimStart('#'), StringComparison.OrdinalIgnoreCase);

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the best text colour for a given background luminance.
    /// Priority: Neutral-role candidates with WCAG contrast ≥ 4.5:1 (highest contrast first).
    /// Fallback: luminance black (#000000) or white (#FFFFFF).
    /// </summary>
    private static string PickTextColor(double bgLuminance, List<AestheticPaletteEntry> neutralCandidates)
    {
        // Try Neutral-role candidates first
        string? bestHex = null;
        var bestContrast = 0.0;
        foreach (var neutral in neutralCandidates)
        {
            var contrast = ContrastRatio(bgLuminance, RelativeLuminance(neutral.Hex));
            if (contrast >= 4.5 && contrast > bestContrast)
            {
                bestContrast = contrast;
                bestHex = neutral.Hex;
            }
        }

        if (bestHex is not null)
        {
            return bestHex;
        }

        // Luminance fallback: black or white, whichever has higher contrast
        var contrastBlack = ContrastRatio(bgLuminance, 0.0);
        var contrastWhite = ContrastRatio(bgLuminance, 1.0);
        return contrastBlack >= contrastWhite ? "#000000" : "#FFFFFF";
    }

    /// <summary>
    /// Parses the role string the multimodal extractor produced.
    ///
    /// <para>
    /// This used to demand exact equality with "primary"/"secondary"/"accent"/"neutral". The model
    /// does not answer that way — it writes descriptive roles like
    /// <c>"primary-accent-and-background (chartreuse/yellow-green)"</c> or
    /// <c>"body text on light backgrounds"</c>. Every one of those failed to parse, which left no
    /// background candidates and silently disabled the entire rotating palette. So matching is by
    /// TOKEN, with a fixed precedence.
    /// </para>
    ///
    /// <para>
    /// Precedence is not cosmetic. <b>Text is tested first</b>, because a body-copy role reading
    /// "body text on light backgrounds" also contains the word "background"; testing background
    /// first would promote the body colour to a slide background. After that the brand hierarchy
    /// decides — primary beats secondary beats accent — so a role claiming several sorts once, at
    /// its most significant position. A bare "background" with no hierarchy word is a real
    /// background but carries no rank information, so it lands in Accent, the last background tier.
    /// </para>
    ///
    /// <para>Anything with no recognisable token stays unparsed and is ignored, as before.</para>
    /// </summary>
    private static ColorRole? TryParseRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var normalized = role.Trim().ToLowerInvariant();

        // Text/neutral FIRST — see the precedence note above.
        if (Mentions(normalized, "neutral") || Mentions(normalized, "text"))
        {
            return ColorRole.Neutral;
        }

        if (Mentions(normalized, "primary"))
        {
            return ColorRole.Primary;
        }

        if (Mentions(normalized, "secondary"))
        {
            return ColorRole.Secondary;
        }

        if (Mentions(normalized, "accent") || Mentions(normalized, "background"))
        {
            return ColorRole.Accent;
        }

        return null;
    }

    private static bool Mentions(string normalizedRole, string token) =>
        normalizedRole.Contains(token, StringComparison.Ordinal);

    /// <summary>
    /// Computes WCAG relative luminance for a hex colour string (#RRGGBB or #RGB).
    /// Returns 0 for unrecognised inputs (treated as black).
    /// Delegates to <see cref="WcagContrast.RelativeLuminance"/> (design D4) — behaviour-identical.
    /// </summary>
    private static double RelativeLuminance(string hex) => WcagContrast.RelativeLuminance(hex);

    /// <summary>
    /// WCAG contrast ratio between two relative luminance values.
    /// Delegates to <see cref="WcagContrast.ContrastRatio(double, double)"/> (design D4) — behaviour-identical.
    /// </summary>
    private static double ContrastRatio(double luminance1, double luminance2) =>
        WcagContrast.ContrastRatio(luminance1, luminance2);
}
