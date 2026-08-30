using System.Reflection;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// CAVS-CG-03 byte-identity regression guard. Phase 0 (WU-0) captures the CURRENT (pre-change)
/// <c>BuildFlatColorMarkup</c>/<c>BuildFlatCarouselSlots</c> output for a representative
/// <see cref="VisualCompositionSpec"/> fixture into
/// <c>Goldens/flat-carousel-cold-start.html</c>, committed against unmodified `dev` HEAD, BEFORE
/// any render code change lands (design decision 6). Every later render-touching work unit
/// (WU-4/WU-5/WU-8) MUST keep this test GREEN for a <c>DecorationMotifs = null</c>
/// (cold-start / non-ClientAesthetic) profile — that is the verified byte-identity contract, not
/// an assertion added after the fact.
/// </summary>
public sealed class FlatCarouselMarkupGoldenTests
{
    private static readonly string GoldenPath = Path.Combine(
        AppContext.BaseDirectory,
        "Application", "Social", "ContentGeneration", "Pipeline", "Goldens", "flat-carousel-cold-start.html");

    [Fact]
    public void ColdStart_BuildFlatColorMarkup_IsByteIdenticalToPreChangeGolden()
    {
        var spec = BuildRepresentativeSpec();
        var palette = new CompositionPalette("#1A2B3C", "#334455", "#66AACC", "#F8FAFC", "#0B1220");
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed");

        var actual = InvokeBuildFlatColorMarkup(spec, palette, aesthetic);
        var golden = File.ReadAllText(GoldenPath);

        Assert.Equal(golden, actual);
    }

    [Fact]
    public void LineWithDiamond_RendersDividerInAccentColour_WithDiamondGlyph()
    {
        var spec = BuildRepresentativeSpec();
        var palette = new CompositionPalette("#1A2B3C", "#334455", "#66AACC", "#F8FAFC", "#0B1220");
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed",
            DecorationMotifs: new DecorationMotifs(DividerRule: DividerRuleStyle.LineWithDiamond));

        var actual = InvokeBuildFlatColorMarkup(spec, palette, aesthetic);

        Assert.Contains("data-slot=\"divider\"", actual, StringComparison.Ordinal);
        Assert.Contains("#66AACC", ExtractDividerElement(actual), StringComparison.Ordinal);
        // Diamond glyph: nested non-slot child, rotated 45deg, no second data-slot attribute.
        Assert.Contains("rotate(45deg)", actual, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(actual, "data-slot=\"divider\""));
    }

    [Fact]
    public void LineWithDot_NoAccentRole_DividerFallsBackToTextColour()
    {
        var spec = BuildRepresentativeSpec();
        var palette = new CompositionPalette("#1A2B3C", "#334455", "#66AACC", "#F8FAFC", "#0B1220");
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed",
            DecorationMotifs: new DecorationMotifs(DividerRule: DividerRuleStyle.LineWithDot));

        var actual = InvokeBuildFlatColorMarkup(spec, palette, aesthetic);

        var dividerElement = ExtractDividerElement(actual);
        Assert.Contains(palette.Text, dividerElement, StringComparison.Ordinal);
        Assert.DoesNotContain("rotate(45deg)", dividerElement, StringComparison.Ordinal);
        Assert.Contains("border-radius:50%", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void NullDividerRule_RendersNoDividerElement()
    {
        var spec = BuildRepresentativeSpec();
        var palette = new CompositionPalette("#1A2B3C", "#334455", "#66AACC", "#F8FAFC", "#0B1220");
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed",
            DecorationMotifs: new DecorationMotifs(DividerRule: null));

        var actual = InvokeBuildFlatColorMarkup(spec, palette, aesthetic);

        Assert.DoesNotContain("data-slot=\"divider\"", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void LeftAlignedArchetype_ReproducesColdStartGeometryExactly()
    {
        var spec = BuildRepresentativeSpec();
        var palette = new CompositionPalette("#1A2B3C", "#334455", "#66AACC", "#F8FAFC", "#0B1220");
        var coldStartAesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed");
        var explicitLeftAligned = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed",
            DecorationMotifs: new DecorationMotifs(CompositionArchetype: "left-aligned"));

        var coldStartMarkup = InvokeBuildFlatColorMarkup(spec, palette, coldStartAesthetic);
        var explicitMarkup = InvokeBuildFlatColorMarkup(spec, palette, explicitLeftAligned);

        Assert.Equal(coldStartMarkup, explicitMarkup);
    }

    [Fact]
    public void CenteredArchetype_ProducesDifferentTitleGeometryThanLeftAligned()
    {
        var spec = BuildRepresentativeSpec();
        var palette = new CompositionPalette("#1A2B3C", "#334455", "#66AACC", "#F8FAFC", "#0B1220");
        var leftAligned = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed");
        var centered = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed",
            DecorationMotifs: new DecorationMotifs(CompositionArchetype: "centered"));

        var leftMarkup = InvokeBuildFlatColorMarkup(spec, palette, leftAligned);
        var centeredMarkup = InvokeBuildFlatColorMarkup(spec, palette, centered);

        Assert.NotEqual(leftMarkup, centeredMarkup);
        Assert.Contains("left:456px", centeredMarkup, StringComparison.Ordinal);
        Assert.Contains("text-align:center", ExtractTitleElement(centeredMarkup), StringComparison.Ordinal);
    }

    [Fact]
    public void UnrecognisedArchetypeToken_FallsBackToLeftAlignedGeometry_NoException()
    {
        var spec = BuildRepresentativeSpec();
        var palette = new CompositionPalette("#1A2B3C", "#334455", "#66AACC", "#F8FAFC", "#0B1220");
        var coldStartAesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed");
        var invented = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed",
            DecorationMotifs: new DecorationMotifs(CompositionArchetype: "diagonal-split"));

        var coldStartMarkup = InvokeBuildFlatColorMarkup(spec, palette, coldStartAesthetic);
        var inventedMarkup = InvokeBuildFlatColorMarkup(spec, palette, invented);

        Assert.Equal(coldStartMarkup, inventedMarkup);
    }

    [Theory]
    [InlineData("left-aligned")]
    [InlineData("centered")]
    [InlineData("lower-third")]
    public void AccentPillMarkup_WidthHeightAndBorderRadius_AreUnchangedAcrossEveryArchetype(string archetype)
    {
        // CAVS-CG-03-b: an archetype changes the accent pill's position (left/top) only — its own
        // markup (width/height/border-radius) is a literal in BuildFlatCarouselSlots, never
        // parameterised by geometry.
        var spec = BuildRepresentativeSpec();
        var palette = new CompositionPalette("#1A2B3C", "#334455", "#66AACC", "#F8FAFC", "#0B1220");
        var aesthetic = new ClientAestheticDesignContext(
            ReferenceImages: [],
            Palette: [new AestheticPaletteEntry("#66AACC", "Accent", 1)],
            StyleDescriptors: [],
            Motifs: [],
            VariationSeed: "golden-seed",
            DecorationMotifs: new DecorationMotifs(CompositionArchetype: archetype));

        var actual = InvokeBuildFlatColorMarkup(spec, palette, aesthetic);
        var accentElement = ExtractAccentElement(actual);

        Assert.Contains("width:168px;height:10px;border-radius:999px;", accentElement, StringComparison.Ordinal);
    }

    private static string ExtractAccentElement(string markup)
    {
        var start = markup.IndexOf("data-slot=\"accent\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "Expected a data-slot=\"accent\" element in the markup.");
        var tagStart = markup.LastIndexOf('<', start);
        var lineEnd = markup.IndexOf('\n', tagStart);
        var end = lineEnd >= 0 ? lineEnd : markup.Length;
        return markup[tagStart..end];
    }

    private static string ExtractTitleElement(string markup)
    {
        var start = markup.IndexOf("data-slot=\"title\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "Expected a data-slot=\"title\" element in the markup.");
        var tagStart = markup.LastIndexOf('<', start);
        var lineEnd = markup.IndexOf('\n', tagStart);
        var end = lineEnd >= 0 ? lineEnd : markup.Length;
        return markup[tagStart..end];
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string ExtractDividerElement(string markup)
    {
        var start = markup.IndexOf("data-slot=\"divider\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "Expected a data-slot=\"divider\" element in the markup.");
        var divStart = markup.LastIndexOf("<div", start, StringComparison.Ordinal);
        // Each slot is emitted on its own line (slots are joined with Environment.NewLine and no
        // slot's own markup embeds a literal newline), so the divider's full element — including its
        // nested glyph child — is exactly the text from its opening tag to the next line break.
        var lineEnd = markup.IndexOf('\n', divStart);
        var end = lineEnd >= 0 ? lineEnd : markup.Length;
        return markup[divStart..end];
    }

    private static string InvokeBuildFlatColorMarkup(
        VisualCompositionSpec spec,
        CompositionPalette palette,
        ClientAestheticDesignContext? aesthetic)
    {
        var method = typeof(ContentGenerationJobProcessor).GetMethod(
            "BuildFlatColorMarkup",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "ContentGenerationJobProcessor.BuildFlatColorMarkup not found via reflection.");

        return (string)method.Invoke(null, [spec, palette, aesthetic])!;
    }

    private static VisualCompositionSpec BuildRepresentativeSpec() => new(
        FormatKey: "carousel-1",
        LayoutType: "carousel",
        Palette: new CompositionPalette("#1A2B3C", "#334455", "#66AACC", "#F8FAFC", "#0B1220"),
        Typography: new CompositionTypography("Inter", 24, "Inter", 14)
        {
            CaptionFamily = "Liberation Serif",
            CaptionWeight = 650,
            CaptionSize = 33,
            MeasuredHeadingSize = 60,
            MeasuredBodySize = 40,
            MeasuredHeadingWeight = 550,
        },
        AssetSlots: new Dictionary<string, AssetSlotSpec>(),
        CopySlots: new Dictionary<string, string>
        {
            ["title"] = "Cold-Start Carousel Title",
            ["subtitle"] = "A representative subtitle for the byte-identity golden fixture.",
            ["cta"] = "Learn More",
        },
        Dimensions: new CompositionDimensions(1080, 1350, 96));
}
