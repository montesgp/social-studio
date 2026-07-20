using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Rendering;
using SkiaSharp;

namespace Incoders.Hive.Tests.Infrastructure.Rendering;

/// <summary>
/// Pixel-sampling GO/NO-GO gate for the Content Studio emoji render spike (design #1168 §1,
/// tasks 1.3/1.4). Asserts real rasterized output, not "something got drawn" — every fact here
/// samples decoded PNG pixels and would fail on tofu or monochrome-outline emoji.
/// </summary>
/// <remarks>
/// All palette colors used in these specs are deliberately grayscale (R == G == B) so that any
/// non-grayscale pixel anywhere in the decoded image can ONLY have come from a color emoji glyph —
/// this removes the need to predict exact glyph pixel coordinates from text-layout internals.
/// </remarks>
[Collection("SkiaRenderingSerial")]
public sealed class EmojiGlyphRenderTests
{
    private const string EmojiSampleText = "Hola \U0001F44B mundo \U0001F389";

    [Fact]
    public async Task RenderAsync_TextContainingEmoji_ProducesNonEmptyColorGlyphPixels()
    {
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildGrayscaleTextSpec(EmojiSampleText);

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var bitmap = SKBitmap.Decode(result.Value.GetBytes());
        Assert.NotNull(bitmap);

        var colorfulPixelCount = CountColorfulPixels(bitmap!);

        // (a) non-empty: at least some pixels are not part of the grayscale palette.
        Assert.True(colorfulPixelCount > 0,
            "Expected at least some non-grayscale (color emoji) pixels in the rendered output.");
    }

    [Fact]
    public async Task RenderAsync_TextContainingEmoji_ProducesMultipleDistinctColors()
    {
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildGrayscaleTextSpec(EmojiSampleText);

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var bitmap = SKBitmap.Decode(result.Value.GetBytes());
        Assert.NotNull(bitmap);

        var distinctColorfulColors = CountDistinctColorfulColors(bitmap!);

        // (c) multi-colour: proves a COLOR emoji glyph rendered, not a monochrome tofu/outline box.
        Assert.True(distinctColorfulColors > 1,
            $"Expected multiple distinct non-grayscale colors (color emoji signature); found {distinctColorfulColors}.");
    }

    [Fact]
    public async Task RenderAsync_TextWithoutEmoji_ProducesNoColorfulPixels()
    {
        // Regression guard / control: no emoji in the text → the grayscale-only palette
        // must stay grayscale end to end. Proves the color pixels in the emoji tests are
        // caused by the emoji glyph, not by anti-aliasing artifacts or gradient blending.
        var renderer = new LocalSvgContentRenderer();
        var spec = BuildGrayscaleTextSpec("Hola mundo sin emoji");

        var result = await renderer.RenderAsync(spec, new RenderOptions(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var bitmap = SKBitmap.Decode(result.Value.GetBytes());
        Assert.NotNull(bitmap);

        var colorfulPixelCount = CountColorfulPixels(bitmap!);

        Assert.Equal(0, colorfulPixelCount);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static VisualCompositionSpec BuildGrayscaleTextSpec(string text) =>
        new VisualCompositionSpec(
            FormatKey: "hook",
            LayoutType: "text-card",
            // Every palette color is grayscale (R == G == B). Any non-grayscale pixel in the
            // decoded output can only come from a rendered color glyph.
            Palette: new CompositionPalette(
                Primary: "#333333",
                Secondary: "#666666",
                Accent: "#999999",
                Text: "#000000",
                Background: "#FFFFFF"),
            Typography: new CompositionTypography("Arial", 48, "Arial", 28),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["text"] = text },
            Dimensions: new CompositionDimensions(1080, 1080, 96));

    /// <summary>
    /// Steps by 2px in both axes for performance; a real color glyph covers far more than a
    /// handful of stray pixels, so downsampling does not risk a false negative.
    /// </summary>
    private static int CountColorfulPixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (IsColorful(bitmap.GetPixel(x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountDistinctColorfulColors(SKBitmap bitmap)
    {
        var colors = new HashSet<SKColor>();
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (IsColorful(pixel))
                {
                    colors.Add(pixel);
                }
            }
        }

        return colors.Count;
    }

    /// <summary>A pixel is "colorful" when R, G, B are not (approximately) equal — i.e. not grayscale.</summary>
    private static bool IsColorful(SKColor color)
    {
        const int tolerance = 6;
        var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        return color.Alpha > 0 && (max - min) > tolerance;
    }
}
