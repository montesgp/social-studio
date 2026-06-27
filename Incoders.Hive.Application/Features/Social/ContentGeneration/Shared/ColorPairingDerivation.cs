using Incoders.Hive.Domain.Social.ClientBrandManuals;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

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
            result.Add(new ColorPairing(entry.Hex, textHex, entry.Rank));
        }

        return result;
    }

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

    private static ColorRole? TryParseRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return role.Trim().ToLowerInvariant() switch
        {
            "primary"   => ColorRole.Primary,
            "secondary" => ColorRole.Secondary,
            "accent"    => ColorRole.Accent,
            "neutral"   => ColorRole.Neutral,
            _           => null,
        };
    }

    /// <summary>
    /// Computes WCAG relative luminance for a hex colour string (#RRGGBB or #RGB).
    /// Returns 0 for unrecognised inputs (treated as black).
    /// </summary>
    private static double RelativeLuminance(string hex)
    {
        hex = hex.TrimStart('#');

        // Expand #RGB → #RRGGBB
        if (hex.Length == 3)
        {
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        }

        if (hex.Length < 6)
        {
            return 0.0;
        }

        try
        {
            var r = Convert.ToInt32(hex[0..2], 16) / 255.0;
            var g = Convert.ToInt32(hex[2..4], 16) / 255.0;
            var b = Convert.ToInt32(hex[4..6], 16) / 255.0;

            return 0.2126 * Linearise(r) + 0.7152 * Linearise(g) + 0.0722 * Linearise(b);
        }
        catch
        {
            return 0.0;
        }
    }

    private static double Linearise(double channel) =>
        channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);

    /// <summary>
    /// WCAG contrast ratio between two relative luminance values.
    /// </summary>
    private static double ContrastRatio(double luminance1, double luminance2)
    {
        var lighter = Math.Max(luminance1, luminance2);
        var darker  = Math.Min(luminance1, luminance2);
        return (lighter + 0.05) / (darker + 0.05);
    }
}
