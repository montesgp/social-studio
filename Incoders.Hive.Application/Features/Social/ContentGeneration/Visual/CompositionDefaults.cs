using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Default palette and typography applied when brand data is missing or incomplete.
/// Neutral grays + accent blue as per WU4 design decision.
/// </summary>
internal static class CompositionDefaults
{
    internal static readonly CompositionPalette Palette = new(
        Primary: "#1F2937",    // dark gray
        Secondary: "#374151",  // medium gray
        Accent: "#3B82F6",     // accent blue
        Text: "#111827",       // near-black text
        Background: "#F9FAFB" // off-white background
    );

    internal static readonly CompositionTypography Typography = new(
        HeadingFamily: "Arial",
        HeadingSize: 28,
        BodyFamily: "Arial",
        BodySize: 16);

    internal static readonly CompositionDimensions StandardDimensions = new(1080, 1080, 96);

    /// <summary>
    /// Resolves a brand palette or returns defaults and marks ColorPalette as inferred.
    /// </summary>
    internal static CompositionPalette ResolvePalette(BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var hasPalette = brand.Brand?.ColorPalette.Count >= 5;
        inferred.Track("brand.ColorPalette", !hasPalette);

        if (!hasPalette)
        {
            return Palette;
        }

        var colors = brand.Brand!.ColorPalette;
        return new CompositionPalette(
            Primary: colors[0],
            Secondary: colors[1],
            Accent: colors[2],
            Text: colors[3],
            Background: colors[4]);
    }

    /// <summary>
    /// Resolves typography from brand manual typography entries or returns defaults.
    /// </summary>
    internal static CompositionTypography ResolveTypography(BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var hasTypography = brand.Brand?.Typography.Count > 0;
        inferred.Track("brand.Typography", !hasTypography);

        if (!hasTypography)
        {
            return Typography;
        }

        // Typography entry is a free-text font family name
        var fontFamily = brand.Brand!.Typography[0];
        return new CompositionTypography(fontFamily, 28, fontFamily, 16);
    }
}
