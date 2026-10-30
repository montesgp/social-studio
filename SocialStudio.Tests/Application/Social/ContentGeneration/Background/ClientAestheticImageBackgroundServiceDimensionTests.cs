using SocialStudio.Application.Features.Social.ContentGeneration.Background;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// Generation-size resolution for ClientAesthetic backgrounds.
///
/// <para>
/// History: the request originally hardcoded a 1024x1024 square, which the composer had to
/// aspect-crop (1:1 -> 4:5) and upscale to fill the 1080x1350 carousel canvas — job 25a65dac
/// produced a visibly pixelated background. The first fix derived the size from the canvas and
/// snapped it to a multiple of 8, which is what <c>EmptyLatentImage</c> validates. That was wrong and
/// stopped image generation entirely: the UNet adds three more 2x downsample stages on top of the
/// VAE, so the real requirement is a multiple of 64.
/// </para>
///
/// <para>
/// Measured on the reference dev backend (ComfyUI 0.3.48, --directml --lowvram --cpu-vae):
/// 1024x1024 succeeds; 1080x1352 OOMs at ~4.0 GB; 912x1144 OOMs at ~4.2 GB despite having FEWER
/// total pixels than 1024x1024; 1024x1280 succeeds with 25% MORE pixels than 1024x1024. Divisibility
/// by 64 — not pixel count — is what decides whether the backend can allocate.
/// </para>
/// </summary>
public sealed class ClientAestheticImageBackgroundServiceDimensionTests
{
    private const int MaxGenerationPixels = 1_310_720;

    [Fact]
    public void ResolveTargetDimensions_Carousel_ResolvesToVerifiedFourByFiveSize()
    {
        // GIVEN the "carousel" format (logical canvas 1080x1350 — CompositionDefaults.CarouselDimensions)
        var (width, height) = ClientAestheticImageBackgroundService.ResolveTargetDimensions("carousel");

        // THEN the resolved size is 1024x1280: an EXACT 4:5 match for the canvas, both dimensions
        // multiples of 64, and the exact size verified to complete on the reference backend.
        Assert.Equal(1024, width);
        Assert.Equal(1280, height);
    }

    [Fact]
    public void ResolveTargetDimensions_Carousel_PreservesCanvasAspectExactly()
    {
        var (width, height) = ClientAestheticImageBackgroundService.ResolveTargetDimensions("carousel");

        // 1080/1350 == 1024/1280 == 0.8. An aspect mismatch is what forced the crop that made the
        // original backgrounds look wrong, so this must stay exact.
        Assert.Equal(1080d / 1350d, (double)width / height, precision: 10);
    }

    [Theory]
    [InlineData("carousel")]
    [InlineData("story-carousel")]
    [InlineData("not-a-real-format")]
    public void ResolveTargetDimensions_AlwaysLandsOnMultipleOfSixtyFour(string formatKey)
    {
        var (width, height) = ClientAestheticImageBackgroundService.ResolveTargetDimensions(formatKey);

        // A multiple of 8 is NOT enough — that is the regression this guards. The UNet needs 64.
        Assert.Equal(0, width % 64);
        Assert.Equal(0, height % 64);
    }

    [Theory]
    [InlineData("carousel")]
    [InlineData("story-carousel")]
    [InlineData("not-a-real-format")]
    public void ResolveTargetDimensions_StaysWithinTheMemoryBudget(string formatKey)
    {
        var (width, height) = ClientAestheticImageBackgroundService.ResolveTargetDimensions(formatKey);

        Assert.True(
            (long)width * height <= MaxGenerationPixels,
            $"{formatKey} resolved to {width}x{height} = {(long)width * height} px, over the verified budget.");
    }

    [Fact]
    public void ResolveGenerationSize_TallStoryCanvas_ShrinksToFitWhileHoldingAspect()
    {
        // GIVEN the 9:16 story canvas, whose full size (2_073_600 px) is well over the budget
        var (width, height) = ClientAestheticImageBackgroundService.ResolveGenerationSize(1080, 1920);

        Assert.Equal(0, width % 64);
        Assert.Equal(0, height % 64);
        Assert.True((long)width * height <= MaxGenerationPixels);

        // Aspect is held close to the 9:16 target rather than letterboxed.
        var resolved = (double)width / height;
        Assert.InRange(resolved, (1080d / 1920d) - 0.02, (1080d / 1920d) + 0.02);
    }

    [Fact]
    public void ResolveGenerationSize_NeverGeneratesLargerThanTheCanvas()
    {
        // A small canvas must not be scaled UP to consume the whole budget — that would waste
        // minutes of generation time producing detail the render throws away.
        var (width, height) = ClientAestheticImageBackgroundService.ResolveGenerationSize(320, 320);

        Assert.True(width <= 320 + 64, $"width {width} unexpectedly exceeds the 320px canvas.");
        Assert.True(height <= 320 + 64, $"height {height} unexpectedly exceeds the 320px canvas.");
    }

    [Fact]
    public void ResolveGenerationSize_DegenerateCanvas_ReturnsOneLatentBlock()
    {
        Assert.Equal((64, 64), ClientAestheticImageBackgroundService.ResolveGenerationSize(0, 0));
        Assert.Equal((64, 64), ClientAestheticImageBackgroundService.ResolveGenerationSize(-10, 100));
    }

    [Theory]
    [InlineData(1024d, 1024)]   // exact multiple -> unchanged
    [InlineData(1080d, 1088)]   // nearest multiple of 64 above
    [InlineData(1050d, 1024)]   // nearest multiple of 64 below — rounding is NEAREST, not always up
    [InlineData(1d, 64)]        // floored to one full latent block
    [InlineData(1280d, 1280)]
    public void SnapToLatentMultiple_RoundsToNearestBlock(double input, int expected)
    {
        Assert.Equal(expected, ClientAestheticImageBackgroundService.SnapToLatentMultiple(input));
    }
}
