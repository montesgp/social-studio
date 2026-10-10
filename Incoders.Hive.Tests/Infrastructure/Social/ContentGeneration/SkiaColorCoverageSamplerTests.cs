using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using SkiaSharp;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// client-style-fidelity CSF-PA-04: the sampler measures how much of a real image a colour covers.
/// </summary>
public sealed class SkiaColorCoverageSamplerTests
{
    [Fact]
    public void FlatSlide_IsFullyCoveredByItsBackground_AndBarelyByItsAccent()
    {
        // A near-black slide with a small lime headline word.
        var slide = Png(400, 500, canvas =>
        {
            canvas.Clear(new SKColor(0x0E, 0x0E, 0x0E));
            using var lime = new SKPaint { Color = new SKColor(0xA6, 0xCE, 0x3C) };
            canvas.DrawRect(40, 200, 160, 40, lime);
        });

        var sampler = SkiaColorCoverageSampler.FromImages([slide]);

        Assert.True(sampler.MaxCoverage("#0E0E0E") > 0.9);
        Assert.True(sampler.MaxCoverage("#A6CE3C") < 0.1);
    }

    [Fact]
    public void NearbyShades_CountAsTheSameBackground()
    {
        // JPEG noise and gentle vignettes shift a flat colour a little; that is still the background.
        var slide = Png(100, 100, canvas => canvas.Clear(new SKColor(0x14, 0x14, 0x14)));

        var sampler = SkiaColorCoverageSampler.FromImages([slide]);

        Assert.True(sampler.MaxCoverage("#0E0E0E") > 0.9);
    }

    [Fact]
    public void Coverage_IsTheBestSingleImage_NotAnAverage()
    {
        var black = Png(100, 100, canvas => canvas.Clear(SKColors.Black));
        var white = Png(100, 100, canvas => canvas.Clear(SKColors.White));

        var sampler = SkiaColorCoverageSampler.FromImages([black, white]);

        Assert.True(sampler.MaxCoverage("#FFFFFF") > 0.9);
    }

    [Fact]
    public void UndecodableImagesAndBadHex_ContributeNothing_AndNeverThrow()
    {
        var sampler = SkiaColorCoverageSampler.FromImages([[1, 2, 3]]);

        Assert.Equal(0, sampler.ImageCount);
        Assert.Equal(0, sampler.MaxCoverage("#000000"));
        Assert.Equal(0, SkiaColorCoverageSampler.FromImages([Png(10, 10, c => c.Clear(SKColors.Black))]).MaxCoverage("not-a-hex"));
    }

    private static byte[] Png(int width, int height, Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            draw(canvas);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
