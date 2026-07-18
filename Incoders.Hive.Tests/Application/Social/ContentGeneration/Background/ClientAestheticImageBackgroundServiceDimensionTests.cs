using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// CAC-IMG-04 (pixelation fix) — job 25a65dac-a31e-4eaf-b9a1-346111f67be7 showed a visibly
/// pixelated ClientAesthetic carousel background: the image-generation request hardcoded a
/// 1024x1024 square target, which the composer then had to upscale ~2.6x and aspect-crop
/// (1:1 -> 4:5) to fill the real 1080x1350 carousel canvas at DeviceScaleFactor=2 (2160x2700
/// physical). These tests drive <see cref="ClientAestheticImageBackgroundService.ResolveTargetDimensions"/>
/// and <see cref="ClientAestheticImageBackgroundService.SnapUpToLatentMultiple"/> directly so the
/// resolved size always matches the format's real canvas (no more hardcoded second constant) and
/// is never smaller than that canvas after the diffusion backend's multiple-of-8 rounding.
/// </summary>
public sealed class ClientAestheticImageBackgroundServiceDimensionTests
{
    [Fact]
    public void ResolveTargetDimensions_Carousel_SnapsHeightUpToMultipleOfEight()
    {
        // GIVEN the "carousel" format (logical canvas 1080x1350 — CompositionDefaults.CarouselDimensions)
        // WHEN resolving the image-generation target size
        var (width, height) = ClientAestheticImageBackgroundService.ResolveTargetDimensions("carousel");

        // THEN width (already a multiple of 8) is unchanged, and height (1350, NOT a multiple of 8)
        // rounds UP to 1352 — never below the logical canvas, and never the old hardcoded 1024x1024.
        Assert.Equal(1080, width);
        Assert.Equal(1352, height);
        Assert.True(height >= 1350, "Resolved height must never be smaller than the logical canvas.");
    }

    [Fact]
    public void ResolveTargetDimensions_StoryCarousel_AlreadyMultipleOfEight_Unchanged()
    {
        // GIVEN the "story-carousel" format (logical canvas 1080x1920 — CompositionDefaults.StoryDimensions),
        // both dimensions are already multiples of 8.
        var (width, height) = ClientAestheticImageBackgroundService.ResolveTargetDimensions("story-carousel");

        Assert.Equal(1080, width);
        Assert.Equal(1920, height);
    }

    [Fact]
    public void ResolveTargetDimensions_UnknownFormatKey_FallsBackToStandardSquare()
    {
        // GIVEN a format key the catalog does not resolve, the resolver falls back to the standard
        // 1080x1080 square (CompositionDefaults.StandardDimensions) rather than throwing.
        var (width, height) = ClientAestheticImageBackgroundService.ResolveTargetDimensions("not-a-real-format");

        Assert.Equal(1080, width);
        Assert.Equal(1080, height);
    }

    [Theory]
    [InlineData(1080, 1080)]   // already a multiple of 8 -> unchanged
    [InlineData(1350, 1352)]   // carousel height -> rounds up
    [InlineData(1, 8)]         // smallest possible input still rounds up to one full multiple
    [InlineData(8, 8)]         // exact multiple -> unchanged
    [InlineData(1920, 1920)]   // story height -> already a multiple of 8
    public void SnapUpToLatentMultiple_RoundsUpNeverDown(int input, int expected)
    {
        Assert.Equal(expected, ClientAestheticImageBackgroundService.SnapUpToLatentMultiple(input));
        Assert.True(
            ClientAestheticImageBackgroundService.SnapUpToLatentMultiple(input) >= input,
            "Snapping must never round DOWN — a smaller-than-canvas image would need an upscale.");
    }
}
