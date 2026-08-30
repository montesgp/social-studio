using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// CAVS-CG-02 / WU-5 task 5.4: apply-time overlap assertion. Design.md marks the non-identity
/// preset pixel values as design proposals, but the binding constraint is that no preset's geometry
/// overlaps the title block or the brand-mark's fixed <c>bottom:56px</c> anchor (CAVS-CG-03-b).
/// </summary>
public sealed class FlatGeometryPresetsTests
{
    // Mirrors the pipeline's fixed title min-height (230px, ContentGenerationJobProcessor.cs) and
    // the brand mark's fixed bottom:56px / 36px-tall anchor box (BuildBrandMarkSlot), against the
    // 1080x1350 canvas the geometry table is authored for.
    private const int TitleMinHeight = 230;
    private const int SubtitleMinHeight = 310;
    private const int CanvasHeight = 1350;
    private const int BrandMarkBottom = 56;
    private const int BrandMarkHeight = 36;

    public static IEnumerable<object[]> AllPresets()
    {
        yield return [nameof(FlatGeometryPresets.LeftAligned), FlatGeometryPresets.LeftAligned];
        yield return [nameof(FlatGeometryPresets.Centered), FlatGeometryPresets.Centered];
        yield return [nameof(FlatGeometryPresets.LowerThird), FlatGeometryPresets.LowerThird];
    }

    [Theory]
    [MemberData(nameof(AllPresets))]
    public void Preset_DividerDoesNotOverlapTitleBlock(string name, FlatCarouselGeometry geometry)
    {
        var titleBottom = geometry.TitleTop + TitleMinHeight;

        Assert.True(
            geometry.DividerTop >= titleBottom,
            $"{name}: divider top {geometry.DividerTop} overlaps the title block (bottom {titleBottom}).");
    }

    [Theory]
    [MemberData(nameof(AllPresets))]
    public void Preset_SubtitleDoesNotOverlapBrandMarkAnchor(string name, FlatCarouselGeometry geometry)
    {
        var subtitleBottom = geometry.SubtitleTop + SubtitleMinHeight;
        var brandMarkTop = CanvasHeight - BrandMarkBottom - BrandMarkHeight;

        Assert.True(
            subtitleBottom <= brandMarkTop,
            $"{name}: subtitle bottom {subtitleBottom} overlaps the brand-mark anchor (top {brandMarkTop}).");
    }

    [Theory]
    [MemberData(nameof(AllPresets))]
    public void Preset_CtaDoesNotOverlapBrandMarkAnchor(string name, FlatCarouselGeometry geometry)
    {
        // Cta is bottom-anchored; the brand mark is also bottom-anchored at bottom:56px/36px tall.
        // No overlap as long as the cta box's bottom offset clears the brand mark's total footprint.
        Assert.True(
            geometry.CtaBottom >= BrandMarkBottom + BrandMarkHeight,
            $"{name}: cta bottom {geometry.CtaBottom} overlaps the brand-mark anchor footprint " +
            $"({BrandMarkBottom + BrandMarkHeight}).");
    }

    [Fact]
    public void Resolve_UnrecognisedToken_ReturnsLeftAlignedIdentityPreset()
    {
        Assert.Same(FlatGeometryPresets.LeftAligned, FlatGeometryPresets.Resolve("diagonal-split"));
        Assert.Same(FlatGeometryPresets.LeftAligned, FlatGeometryPresets.Resolve(null));
        Assert.Same(FlatGeometryPresets.LeftAligned, FlatGeometryPresets.Resolve("left-aligned"));
    }

    [Fact]
    public void Resolve_RecognisedTokens_ReturnCaseInsensitiveMatchingPresets()
    {
        Assert.Same(FlatGeometryPresets.Centered, FlatGeometryPresets.Resolve("Centered"));
        Assert.Same(FlatGeometryPresets.LowerThird, FlatGeometryPresets.Resolve(" lower-third "));
    }
}
