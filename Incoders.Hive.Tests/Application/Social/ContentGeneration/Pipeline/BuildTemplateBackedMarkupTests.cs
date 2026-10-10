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
/// client-aesthetic-generation-hardening WU-1 (CAGH-CG-01): <c>BuildStoryTemplateSlots</c> and
/// <c>BuildCarouselTemplateSlots</c> consume the resolved <c>MeasuredHeadingSize</c>/<c>MeasuredBodySize</c>
/// when present, falling back to the pre-change hardcoded literals (88px/74px/38px/36px/30px) when
/// absent — cold-start parity.
/// </summary>
public sealed class BuildTemplateBackedMarkupTypographyTests
{
    private static readonly CompositionPalette Palette = new(
        Primary: "#333333", Secondary: "#666666", Accent: "#999999", Text: "#FFFFFF", Background: "#ABCDEF");

    [Fact]
    public void StorySlide_MeasuredSizesPresent_RendersMeasuredHeadingAndBodySizes()
    {
        var typography = new CompositionTypography("Inter", 24, "Inter", 14)
        {
            MeasuredHeadingSize = 96,
            MeasuredBodySize = 33,
        };
        var spec = BuildSpec("story", typography, new Dictionary<string, string>
        {
            ["headline"] = "Title",
            ["subtitle"] = "Sub",
            ["body"] = "Body",
        });

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background: null, aesthetic: null, transparency: null);

        Assert.Contains("font-size:96px", markup); // headline uses MeasuredHeadingSize
        Assert.Contains("font-size:33px", markup); // subtitle + body use MeasuredBodySize
        Assert.DoesNotContain("font-size:88px", markup);
        Assert.DoesNotContain("font-size:36px", markup);
        Assert.DoesNotContain("font-size:30px", markup);
    }

    [Fact]
    public void StorySlide_MeasuredSizesAbsent_RendersPreChangeLiterals()
    {
        var typography = new CompositionTypography("Inter", 24, "Inter", 14);
        var spec = BuildSpec("story", typography, new Dictionary<string, string>
        {
            ["headline"] = "Title",
            ["subtitle"] = "Sub",
            ["body"] = "Body",
        });

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background: null, aesthetic: null, transparency: null);

        Assert.Contains("font-size:88px", markup);
        Assert.Contains("font-size:36px", markup);
        Assert.Contains("font-size:30px", markup);
    }

    [Fact]
    public void CarouselSlide_MeasuredSizesPresent_RendersMeasuredHeadingAndBodySizes()
    {
        var typography = new CompositionTypography("Inter", 24, "Inter", 14)
        {
            MeasuredHeadingSize = 60,
            MeasuredBodySize = 40,
        };
        var spec = BuildSpec("carousel", typography, new Dictionary<string, string>
        {
            ["title"] = "Title",
            ["subtitle"] = "Sub",
        });

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background: null, aesthetic: null, transparency: null);

        Assert.Contains("font-size:60px", markup);
        Assert.Contains("font-size:40px", markup);
        Assert.DoesNotContain("font-size:74px", markup);
        Assert.DoesNotContain("font-size:38px", markup);
    }

    [Fact]
    public void CarouselSlide_MeasuredSizesAbsent_RendersPreChangeLiterals()
    {
        var typography = new CompositionTypography("Inter", 24, "Inter", 14);
        var spec = BuildSpec("carousel", typography, new Dictionary<string, string>
        {
            ["title"] = "Title",
            ["subtitle"] = "Sub",
        });

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background: null, aesthetic: null, transparency: null);

        Assert.Contains("font-size:74px", markup);
        Assert.Contains("font-size:38px", markup);
    }

    [Fact]
    public void TwoCarouselClients_DifferentMeasuredHeadingSize_RenderDifferentFontSizes()
    {
        // CAGH-CG-01-c
        var typographyA = new CompositionTypography("Inter", 24, "Inter", 14) { MeasuredHeadingSize = 60 };
        var typographyB = new CompositionTypography("Inter", 24, "Inter", 14) { MeasuredHeadingSize = 90 };
        var copySlots = new Dictionary<string, string> { ["title"] = "Title" };

        var markupA = BuildTemplateBackedMarkupAccessor.Invoke(
            BuildSpec("carousel", typographyA, copySlots), background: null, aesthetic: null, transparency: null);
        var markupB = BuildTemplateBackedMarkupAccessor.Invoke(
            BuildSpec("carousel", typographyB, copySlots), background: null, aesthetic: null, transparency: null);

        Assert.Contains("font-size:60px", markupA);
        Assert.Contains("font-size:90px", markupB);
        Assert.NotEqual(markupA, markupB);
    }

    private static VisualCompositionSpec BuildSpec(
        string formatKey, CompositionTypography typography, Dictionary<string, string> copySlots) => new(
        FormatKey: formatKey,
        LayoutType: "story-story",
        Palette: Palette,
        Typography: typography,
        AssetSlots: new Dictionary<string, AssetSlotSpec>(),
        CopySlots: copySlots,
        Dimensions: new CompositionDimensions(1080, 1350, 96));
}

/// <summary>
/// WU-3 task 3.2/3.3/3.4 — enforced-photo text colour resolves from <c>spec.Palette.Text</c>
/// (design D3) only when it clears WCAG ≥ 4.5:1 against black; otherwise the pre-existing
/// <c>color:#FFFFFF</c> + text-shadow fallback applies unchanged. Shadow is always retained.
/// Refs: CAGH-CG-03-a/b/c.
/// </summary>
public sealed class BuildTemplateBackedMarkupTextColorTests
{
    private static readonly CompositionTypography Typography = new("Inter", 24, "Inter", 14);

    [Fact]
    public void StorySlide_PaletteTextClearsContrastGuard_RendersPairingColor_NoWhiteFallback()
    {
        var palette = new CompositionPalette(
            Primary: "#333333", Secondary: "#666666", Accent: "#999999", Text: "#E0E0E0", Background: "#ABCDEF");
        var spec = BuildSpec("story", palette);
        var background = BuildTemplateBackedMarkupAccessor.BuildEnforcedBackground(
            "https://cdn/photo.jpg", "Client photo", "brand-manual-forced-default");

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background, aesthetic: null, transparency: null);

        Assert.Contains("color:#E0E0E0", markup);
        Assert.DoesNotContain("color:#FFFFFF", markup);
        // Shadow retained regardless of which colour rendered (D3).
        Assert.Contains("text-shadow", markup);
    }

    [Fact]
    public void CarouselSlide_PaletteTextClearsContrastGuard_RendersPairingColor_NoWhiteFallback()
    {
        var palette = new CompositionPalette(
            Primary: "#333333", Secondary: "#666666", Accent: "#999999", Text: "#E0E0E0", Background: "#ABCDEF");
        var spec = BuildSpec("carousel", palette);
        var background = BuildTemplateBackedMarkupAccessor.BuildEnforcedBackground(
            "https://cdn/photo.jpg", "Client photo", "brand-manual-forced-default");

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background, aesthetic: null, transparency: null);

        Assert.Contains("color:#E0E0E0", markup);
        Assert.DoesNotContain("color:#FFFFFF", markup);
        Assert.Contains("text-shadow", markup);
    }

    [Fact]
    public void StorySlide_PaletteTextFailsContrastGuard_FallsBackToWhiteWithShadow_Regression()
    {
        // Cold-start / default palette Text ("#111827", near-black) fails 4.5:1 against black.
        var palette = CompositionDefaults.Palette;
        var spec = BuildSpec("story", palette);
        var background = BuildTemplateBackedMarkupAccessor.BuildEnforcedBackground(
            "https://cdn/photo.jpg", "Client photo", "brand-manual-forced-default");

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background, aesthetic: null, transparency: null);

        Assert.DoesNotContain("color:#111827", markup);
        Assert.Contains("color:#FFFFFF", markup);
        Assert.Contains("text-shadow", markup);
    }

    [Fact]
    public void CarouselSlide_PaletteTextFailsContrastGuard_FallsBackToWhiteWithShadow_Regression()
    {
        var palette = CompositionDefaults.Palette;
        var spec = BuildSpec("carousel", palette);
        var background = BuildTemplateBackedMarkupAccessor.BuildEnforcedBackground(
            "https://cdn/photo.jpg", "Client photo", "brand-manual-forced-default");

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background, aesthetic: null, transparency: null);

        Assert.DoesNotContain("color:#111827", markup);
        Assert.Contains("color:#FFFFFF", markup);
        Assert.Contains("text-shadow", markup);
    }

    [Fact]
    public void StorySlide_LowButGuardClearingPairingColor_StillRendersPairingColor_ShadowCompensates()
    {
        // A pale mid-tone that clears 4.5:1 against black but would be visually low-contrast
        // against a bright photo (unknowable at markup time, design D3 rationale) — still renders
        // as the pairing colour, never silently replaced by white; the shadow is the compensation.
        var palette = new CompositionPalette(
            Primary: "#333333", Secondary: "#666666", Accent: "#999999", Text: "#B0B0B0", Background: "#ABCDEF");
        var spec = BuildSpec("story", palette);
        var background = BuildTemplateBackedMarkupAccessor.BuildEnforcedBackground(
            "https://cdn/photo.jpg", "Client photo", "brand-manual-forced-default");

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, background, aesthetic: null, transparency: null);

        Assert.Contains("color:#B0B0B0", markup);
        Assert.DoesNotContain("color:#FFFFFF", markup);
        Assert.Contains("text-shadow", markup);
    }

    private static VisualCompositionSpec BuildSpec(string formatKey, CompositionPalette palette) => new(
        FormatKey: formatKey,
        LayoutType: "story-story",
        Palette: palette,
        Typography: Typography,
        AssetSlots: new Dictionary<string, AssetSlotSpec>(),
        CopySlots: new Dictionary<string, string> { ["title"] = "Test", ["headline"] = "Test" },
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

/// <summary>
/// client-style-fidelity CSF-CG-01/02/04/05: the template is chosen by layout, a ClientAesthetic
/// slide draws no decoration the client was not measured to use, and measured typography keeps the
/// typeface's own spacing and the client's headline width.
/// </summary>
public sealed class BuildTemplateBackedMarkupClientStyleTests
{
    private static readonly CompositionPalette Palette = new(
        Primary: "#333333", Secondary: "#666666", Accent: "#A6CE3C", Text: "#FFFFFF", Background: "#0E0E0E");

    private static readonly ClientAestheticDesignContext Aesthetic = new(
        ReferenceImages: [],
        Palette: [new AestheticPaletteEntry("#A6CE3C", "accent", 1)],
        StyleDescriptors: [],
        Motifs: [],
        VariationSeed: "seed");

    [Fact]
    public void FeedCarousel_UsesTheCarouselTemplate_NotTheStoryTemplate()
    {
        // The bug behind the stray bar on photo slides: "feed-carousel" was compared against the
        // legacy "carousel" key only, so real carousels rendered with the Story template and its pill.
        var markup = BuildTemplateBackedMarkupAccessor.Invoke(
            BuildSpec("feed-carousel", new CompositionTypography("Inter", 24, "Inter", 14)),
            Photo(), aesthetic: null, transparency: null);

        Assert.Contains("data-slot=\"title\"", markup);
        Assert.DoesNotContain("data-slot=\"headline\"", markup);
        Assert.DoesNotContain("data-slot=\"accent\"", markup);
    }

    [Theory]
    [InlineData("feed-carousel")]
    [InlineData("story")]
    public void ClientAestheticSlide_DrawsNoPillAndNoBar(string formatKey)
    {
        var markup = BuildTemplateBackedMarkupAccessor.Invoke(
            BuildSpec(formatKey, new CompositionTypography("Inter", 24, "Inter", 14)),
            Photo(), Aesthetic, transparency: null);

        Assert.DoesNotContain("data-slot=\"accent\"", markup);
        Assert.DoesNotContain("border-radius:999px", markup);
    }

    [Fact]
    public void ColdStartCarousel_KeepsItsDecorationUnchanged()
    {
        var markup = BuildTemplateBackedMarkupAccessor.Invoke(
            BuildSpec("feed-carousel", new CompositionTypography("Inter", 24, "Inter", 14)),
            Photo(), aesthetic: null, transparency: null);

        Assert.Contains("bottom:140px;height:10px;border-radius:999px", markup);
        Assert.Contains("letter-spacing:-1.8px;", markup);
        Assert.Contains("width:872px;", markup);
    }

    [Fact]
    public void MeasuredTypography_UsesNeutralTracking_MeasuredWidth_AndBodyWeight()
    {
        var typography = new CompositionTypography("League Gothic", 24, "Poppins", 14)
        {
            MeasuredHeadingSize = 122,
            MeasuredHeadingWeight = 400,
            MeasuredHeadingBlockWidth = 540,
            MeasuredBodyWeight = 300,
        };

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(
            BuildSpec("feed-carousel", typography), Photo(), Aesthetic, transparency: null);

        Assert.Contains("width:540px;text-wrap:balance;", markup);
        Assert.Contains("letter-spacing:0;", markup);
        Assert.DoesNotContain("letter-spacing:-1.8px", markup);
        Assert.Contains("font-weight:300;", markup);
    }

    [Fact]
    public void Highlight_WrapsTheChosenWords_InTheObservedColour()
    {
        var spec = BuildSpec("feed-carousel", new CompositionTypography("Inter", 24, "Inter", 14)) with
        {
            CopySlots = new Dictionary<string, string> { ["title"] = "¿Sabés qué toman tus clientes en tu local?" },
            TextHighlights = new Dictionary<string, string> { ["title"] = "Tus Clientes" },
            HighlightColor = "#A6CE3C",
        };

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, Photo(), Aesthetic, transparency: null);

        Assert.Contains("<span data-highlight=\"true\" style=\"color:#A6CE3C;\">tus clientes</span>", markup);
    }

    [Theory]
    [InlineData("no aparece", "#A6CE3C")]          // not part of the title
    [InlineData("tus clientes", null)]              // the client does not highlight
    [InlineData("tus clientes", "red;background:x")] // unsafe colour
    public void Highlight_IsIgnored_WhenItCannotBeAppliedSafely(string highlight, string? colour)
    {
        var spec = BuildSpec("feed-carousel", new CompositionTypography("Inter", 24, "Inter", 14)) with
        {
            CopySlots = new Dictionary<string, string> { ["title"] = "¿Sabés qué toman tus clientes?" },
            TextHighlights = new Dictionary<string, string> { ["title"] = highlight },
            HighlightColor = colour,
        };

        var markup = BuildTemplateBackedMarkupAccessor.Invoke(spec, Photo(), Aesthetic, transparency: null);

        Assert.DoesNotContain("data-highlight", markup);
    }

    private static object Photo() =>
        BuildTemplateBackedMarkupAccessor.BuildEnforcedBackground(
            "https://cdn/photo.jpg", "Client photo", "brand-manual-forced-default");

    private static VisualCompositionSpec BuildSpec(string formatKey, CompositionTypography typography) => new(
        FormatKey: formatKey,
        LayoutType: "grid",
        Palette: Palette,
        Typography: typography,
        AssetSlots: new Dictionary<string, AssetSlotSpec>(),
        CopySlots: new Dictionary<string, string> { ["title"] = "Title", ["headline"] = "Title", ["subtitle"] = "Sub" },
        Dimensions: new CompositionDimensions(1080, 1350, 96));
}
