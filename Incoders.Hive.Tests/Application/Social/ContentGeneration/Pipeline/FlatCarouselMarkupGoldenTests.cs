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
