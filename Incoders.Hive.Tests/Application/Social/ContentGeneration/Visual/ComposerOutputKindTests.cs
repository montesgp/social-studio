using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// T1.3 TDD — asserts text format composers set OutputKind=Text and populate TextBody;
/// raster composers (flyer, carousel) leave OutputKind=Raster.
/// Tested via the public <see cref="VisualCompositionService"/> API (composers are internal).
/// </summary>
public sealed class ComposerOutputKindTests
{
    private static readonly BrandSnapshot NoBrand = BrandSnapshot.FromBrandData(null, null, null);

    // ------------------------------------------------------------------
    // Text-only composers: OutputKind=Text, TextBody non-null/non-empty
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("reel-script")]
    [InlineData("caption")]
    [InlineData("hook")]
    [InlineData("content-idea")]
    [InlineData("cta-variation")]
    public async Task TextFormatComposer_Compose_SetsOutputKindText(string formatKey)
    {
        var service = new VisualCompositionService();

        var spec = await service.ComposeAsync(formatKey, new { }, NoBrand, CancellationToken.None);

        Assert.Equal(CompositionOutputKind.Text, spec.OutputKind);
    }

    [Theory]
    [InlineData("reel-script")]
    [InlineData("caption")]
    [InlineData("hook")]
    [InlineData("content-idea")]
    [InlineData("cta-variation")]
    public async Task TextFormatComposer_Compose_PopulatesTextBody(string formatKey)
    {
        var service = new VisualCompositionService();

        var spec = await service.ComposeAsync(formatKey, new { }, NoBrand, CancellationToken.None);

        Assert.NotNull(spec.TextBody);
        Assert.NotEmpty(spec.TextBody);
    }

    // ------------------------------------------------------------------
    // Raster composers: OutputKind=Raster (default), TextBody null
    // ------------------------------------------------------------------

    [Fact]
    public async Task FlyerComposer_Compose_OutputKindIsRaster()
    {
        var service = new VisualCompositionService();

        var spec = await service.ComposeAsync("flyer", new { }, NoBrand, CancellationToken.None);

        Assert.Equal(CompositionOutputKind.Raster, spec.OutputKind);
        Assert.Null(spec.TextBody);
    }

    [Fact]
    public async Task CarouselComposer_Compose_OutputKindIsRaster()
    {
        var service = new VisualCompositionService();

        var spec = await service.ComposeAsync("carousel", new { }, NoBrand, CancellationToken.None);

        Assert.Equal(CompositionOutputKind.Raster, spec.OutputKind);
        Assert.Null(spec.TextBody);
    }
}
