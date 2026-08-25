using System.Reflection;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
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

/// <summary>
/// WU-3b (D3 render half + D5) — task 3b.5: <c>BuildTemplateBackedMarkup</c>'s per-slide transparency
/// treatment. Signal true → the enforced-background <c>&lt;img&gt;</c> carries
/// <c>opacity:{CompositionDefaults.BlendedPhotoOpacity}</c> composited over a palette-derived backdrop.
/// Signal absent/false → today's unconditional <c>object-fit:cover</c>, byte-identical, no opacity
/// token. Satisfies CAFF-CG-05.
/// </summary>
public sealed class BuildTemplateBackedMarkupTransparencyTests
{
    private static readonly CompositionPalette Palette = new(
        Primary: "#333333", Secondary: "#666666", Accent: "#999999", Text: "#FFFFFF", Background: "#ABCDEF");

    private static readonly CompositionTypography Typography = new("Inter", 24, "Inter", 14);

    [Fact]
    public void BlendedTrue_EnforcedBackground_AppliesOpacityOverPaletteBackdrop()
    {
        var spec = BuildSpec();
        var background = BuildTemplateBackedMarkupAccessor.BuildEnforcedBackground(
            "https://cdn/photo.jpg", "Client photo", "brand-manual-forced-default");
        var transparency = new SlideBackgroundTransparency(true, "faded overlay");

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background, aesthetic: null, transparency);

        Assert.Contains("opacity:0.72", markup);
        // The reduced-alpha photo composites against the palette background, not the #000000 root.
        Assert.Contains("#ABCDEF", markup);
    }

    [Fact]
    public void BlendedFalse_EnforcedBackground_NoOpacityToken_UnchangedObjectFitCover()
    {
        var spec = BuildSpec();
        var background = BuildTemplateBackedMarkupAccessor.BuildEnforcedBackground(
            "https://cdn/photo.jpg", "Client photo", "brand-manual-forced-default");
        var transparency = new SlideBackgroundTransparency(false, null);

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background, aesthetic: null, transparency);

        Assert.DoesNotContain("opacity:", markup);
        Assert.Contains(
            """style="position:absolute;inset:0;width:100%;height:100%;object-fit:cover;" """.Trim(),
            markup);
    }

    [Fact]
    public void NullTransparency_EnforcedBackground_ColdStart_UnchangedObjectFitCover()
    {
        var spec = BuildSpec();
        var background = BuildTemplateBackedMarkupAccessor.BuildEnforcedBackground(
            "https://cdn/photo.jpg", "Client photo", "brand-manual-forced-default");

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background, aesthetic: null, transparency: null);

        Assert.DoesNotContain("opacity:", markup);
        Assert.Contains(
            """style="position:absolute;inset:0;width:100%;height:100%;object-fit:cover;" """.Trim(),
            markup);
    }

    private static VisualCompositionSpec BuildSpec() => new(
        FormatKey: "carousel",
        LayoutType: "story-story",
        Palette: Palette,
        Typography: Typography,
        AssetSlots: new Dictionary<string, AssetSlotSpec>(),
        CopySlots: new Dictionary<string, string> { ["title"] = "Test" },
        Dimensions: new CompositionDimensions(1080, 1350, 96));
}

/// <summary>
/// Test accessor for the private static <c>ContentGenerationJobProcessor.BuildTemplateBackedMarkup</c>,
/// mirroring <c>ResolveEnforcedBackgroundAccessor</c>'s pattern: reflection so the production class
/// stays un-modified while the test pins the exact behavioral contract.
/// </summary>
file static class BuildTemplateBackedMarkupAccessor
{
    private static readonly MethodInfo Method;
    private static readonly Type EnforcedBackgroundTemplateType;

    static BuildTemplateBackedMarkupAccessor()
    {
        Method = typeof(ContentGenerationJobProcessor)
            .GetMethod(
                "BuildTemplateBackedMarkup",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "ContentGenerationJobProcessor.BuildTemplateBackedMarkup not found. " +
                "If the method was renamed or removed, update this accessor.");

        EnforcedBackgroundTemplateType = typeof(ContentGenerationJobProcessor)
            .GetNestedType("EnforcedBackgroundTemplate", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "ContentGenerationJobProcessor.EnforcedBackgroundTemplate not found. " +
                "If the type was renamed or removed, update this accessor.");
    }

    public static object BuildEnforcedBackground(string url, string name, string source) =>
        Activator.CreateInstance(EnforcedBackgroundTemplateType, url, name, source)!;

    public static string Invoke(
        VisualCompositionSpec spec,
        object? background,
        ClientAestheticDesignContext? aesthetic,
        SlideBackgroundTransparency? transparency) =>
        (string)Method.Invoke(null, [spec, background, aesthetic, transparency])!;
}
