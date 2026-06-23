using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// Step 1.10 — assert that <see cref="CompositionDefaults.ResolvePalette"/> sources gradient
/// hex values from <see cref="WeightedColor.Hex"/>, not from any string-based palette representation.
/// <para>
/// <c>BuildTemplateBackedMarkup</c> (in ContentGenerationJobProcessor) calls
/// <c>ResolvePalette</c> to obtain <c>spec.Palette.Primary</c> / <c>.Secondary</c> which are used
/// directly in the linear-gradient fallback. This test drives the exact extraction path.
/// </para>
/// </summary>
public sealed class BuildTemplateBackedMarkupTests
{
    [Fact]
    public void ResolvePalette_WeightedColorPrimary_IsExtractedFromHexProperty()
    {
        // GIVEN a brand with a distinct primary WeightedColor hex (#EE1122)
        var brand = BuildBrandSnapshot(
            new WeightedColor("#EE1122", ColorRole.Primary, 0),
            new WeightedColor("#002244", ColorRole.Secondary, 1));

        // WHEN palette is resolved via CompositionDefaults
        var accumulator = new InferenceAccumulator();
        var palette = CompositionDefaults.ResolvePalette(brand, accumulator);

        // THEN the primary hex comes from WeightedColor.Hex, not from the record's ToString()
        Assert.Equal("#EE1122", palette.Primary);
        Assert.Equal("#002244", palette.Secondary);
    }

    [Fact]
    public void ResolvePalette_WeightedColorWithNullRole_HexStillExtracted()
    {
        // GIVEN palette entries without an assigned role (role-agnostic entries)
        var brand = BuildBrandSnapshot(
            new WeightedColor("#AA3300", null, 0),
            new WeightedColor("#0044BB", null, 1));

        var accumulator = new InferenceAccumulator();
        var palette = CompositionDefaults.ResolvePalette(brand, accumulator);

        // THEN hex is still correctly extracted regardless of role
        Assert.Equal("#AA3300", palette.Primary);
        Assert.Equal("#0044BB", palette.Secondary);
    }

    [Fact]
    public void ResolvePalette_EmptyWeightedPalette_MarksColorPaletteAsInferred()
    {
        // GIVEN a brand with an empty WeightedColor palette
        var brand = BuildBrandSnapshot();

        var accumulator = new InferenceAccumulator();
        CompositionDefaults.ResolvePalette(brand, accumulator);

        // THEN ColorPalette is marked inferred (brand has no color data)
        Assert.True(accumulator.Fields.Contains("brand.ColorPalette"),
            "ResolvePalette must mark brand.ColorPalette as inferred when WeightedColor palette is empty.");
    }

    [Fact]
    public void ResolvePalette_NonEmptyWeightedPalette_DoesNotMarkAsInferred()
    {
        // GIVEN a brand with at least one WeightedColor
        var brand = BuildBrandSnapshot(new WeightedColor("#112233", null, 0));

        var accumulator = new InferenceAccumulator();
        CompositionDefaults.ResolvePalette(brand, accumulator);

        // THEN ColorPalette is NOT marked inferred
        Assert.False(accumulator.Fields.Contains("brand.ColorPalette"),
            "ResolvePalette must NOT mark brand.ColorPalette as inferred when WeightedColor entries exist.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static BrandSnapshot BuildBrandSnapshot(params WeightedColor[] palette) =>
        BrandSnapshot.FromBrandData(
            new BrandManualSnapshot(
                ColorPalette: palette,
                Logos: Array.Empty<string>(),
                BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
                Typography: new[] { "Inter" },
                VoiceAndTone: null,
                Notes: null),
            null, null);
}
