using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
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
    /// Feed carousel slides use the modern 4:5 feed ratio (1080×1350) — more screen real-estate in the
    /// Instagram feed than the legacy 1:1 square. Stories use 9:16; text cards stay square.
    /// </summary>
    internal static readonly CompositionDimensions CarouselDimensions = new(1080, 1350, 96);

    /// <summary>
    /// Story canvas — full-screen 9:16 (1080×1920). Single stories and story-carousel slides both use
    /// this so the editable design fills the story frame instead of leaving dead space.
    /// </summary>
    internal static readonly CompositionDimensions StoryDimensions = new(1080, 1920, 96);

    /// <summary>
    /// Resolves a brand palette or returns defaults and marks ColorPalette as inferred.
    /// </summary>
    internal static CompositionPalette ResolvePalette(BrandSnapshot brand, InferenceAccumulator inferred)
    {
        var colors = brand.Brand?.ColorPalette
            .Where(wc => !string.IsNullOrWhiteSpace(wc.Hex))
            .Select(wc => wc.Hex.Trim())
            .ToArray() ?? [];

        var hasPalette = colors.Length > 0;
        inferred.Track("brand.ColorPalette", !hasPalette);

        if (!hasPalette)
        {
            return Palette;
        }

        return new CompositionPalette(
            Primary: Pick(colors, 0, Palette.Primary),
            Secondary: Pick(colors, 1, Palette.Secondary),
            Accent: Pick(colors, 2, Palette.Accent),
            Text: Pick(colors, 3, Palette.Text),
            Background: Pick(colors, 4, Palette.Background));
    }

    /// <summary>
    /// Resolves a palette using the client's ranked rotating colour pairings when present,
    /// otherwise delegates to the existing positional <see cref="ResolvePalette(BrandSnapshot, InferenceAccumulator)"/>.
    ///
    /// Rotation formula: <c>pairings[(slideIndex-1) % pairings.Count]</c>.
    /// When <paramref name="profile"/> is null or <c>ColorPairings</c> is null/empty, the result is
    /// byte-identical to <see cref="ResolvePalette(BrandSnapshot, InferenceAccumulator)"/> (cold-start parity).
    ///
    /// Refs: CAC-CG-02; design §3.3, §0 OQ-2.
    /// </summary>
    internal static CompositionPalette ResolvePalette(
        BrandSnapshot brand,
        ClientAestheticProfile? profile,
        int slideIndex,
        InferenceAccumulator inferred)
    {
        var pairings = profile?.ColorPairings;
        if (pairings is { Count: > 0 })
        {
            var p = pairings[(slideIndex - 1) % pairings.Count];
            var basePalette = ResolvePalette(brand, inferred);
            return basePalette with
            {
                Background = p.BackgroundHex,
                Text = p.TextHex,
            };
        }

        return ResolvePalette(brand, inferred);
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

    private static string Pick(IReadOnlyList<string> values, int index, string fallback) =>
        index < values.Count ? values[index] : fallback;
}
