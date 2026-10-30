namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Shared WCAG 2.x relative-luminance / contrast-ratio maths. Extracted from
/// <see cref="ColorPairingDerivation"/>'s formerly-private helpers (design D4) so a second
/// consumer (the enforced-photo text-colour guard, CAGH-CG-03) does not duplicate the algorithm.
/// Pure Application logic — no provider/EF/Infrastructure types.
/// </summary>
public static class WcagContrast
{
    /// <summary>
    /// Computes WCAG relative luminance for a hex colour string (#RRGGBB or #RGB).
    /// Returns 0 for unrecognised inputs (treated as black).
    /// </summary>
    public static double RelativeLuminance(string hex)
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

    /// <summary>
    /// WCAG contrast ratio between two relative luminance values.
    /// </summary>
    public static double ContrastRatio(double luminance1, double luminance2)
    {
        var lighter = Math.Max(luminance1, luminance2);
        var darker = Math.Min(luminance1, luminance2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// WCAG contrast ratio between two hex colour strings — convenience overload for call sites
    /// that only have hex values (e.g. the enforced-photo text-colour guard, CAGH-CG-03).
    /// </summary>
    public static double ContrastRatio(string hex1, string hex2) =>
        ContrastRatio(RelativeLuminance(hex1), RelativeLuminance(hex2));

    private static double Linearise(double channel) =>
        channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
