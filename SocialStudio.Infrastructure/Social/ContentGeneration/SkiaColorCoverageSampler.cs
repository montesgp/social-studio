using System.Globalization;
using SkiaSharp;

namespace SocialStudio.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Measures how much of each reference image a colour covers (client-style-fidelity CSF-PA-04).
/// Images are decoded once and downsampled, because the question — "is this a background this client
/// actually uses?" — is about large uniform areas, not detail.
/// </summary>
internal sealed class SkiaColorCoverageSampler
{
    /// <summary>Downsampled width; plenty to measure a full-bleed background, cheap to scan.</summary>
    private const int SampleWidth = 72;

    /// <summary>
    /// RGB distance within which a pixel counts as "this colour". Wide enough to absorb JPEG noise and
    /// gentle vignettes on a flat slide, narrow enough that near-black never matches a lime green.
    /// </summary>
    private const int MaxDistance = 48;

    private readonly IReadOnlyList<SKColor[]> _images;

    private SkiaColorCoverageSampler(IReadOnlyList<SKColor[]> images)
    {
        _images = images;
    }

    /// <summary>Decodes every image it can; undecodable bytes are skipped, never thrown.</summary>
    public static SkiaColorCoverageSampler FromImages(IEnumerable<byte[]> images)
    {
        var decoded = new List<SKColor[]>();
        foreach (var bytes in images)
        {
            var pixels = Downsample(bytes);
            if (pixels is { Length: > 0 })
            {
                decoded.Add(pixels);
            }
        }

        return new SkiaColorCoverageSampler(decoded);
    }

    public int ImageCount => _images.Count;

    /// <summary>
    /// Largest share (0..1) of any single image covered by colours close to <paramref name="hex"/>.
    /// Zero for an unparseable hex or when no image could be decoded.
    /// </summary>
    public double MaxCoverage(string hex)
    {
        if (!TryParseHex(hex, out var target))
        {
            return 0;
        }

        var best = 0.0;
        foreach (var pixels in _images)
        {
            var matching = 0;
            foreach (var pixel in pixels)
            {
                if (IsClose(pixel, target))
                {
                    matching++;
                }
            }

            best = Math.Max(best, (double)matching / pixels.Length);
        }

        return best;
    }

    private static bool IsClose(SKColor a, SKColor b)
    {
        var dr = a.Red - b.Red;
        var dg = a.Green - b.Green;
        var db = a.Blue - b.Blue;
        return dr * dr + dg * dg + db * db <= MaxDistance * MaxDistance;
    }

    private static SKColor[]? Downsample(byte[] bytes)
    {
        try
        {
            using var source = SKBitmap.Decode(bytes);
            if (source is null || source.Width <= 0 || source.Height <= 0)
            {
                return null;
            }

            var height = Math.Max(1, (int)Math.Round(source.Height * (double)SampleWidth / source.Width));
            using var scaled = source.Resize(
                new SKImageInfo(SampleWidth, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKFilterMode.Linear));
            return scaled?.Pixels;
        }
        catch (Exception)
        {
            // A corrupt or unsupported reference image must never fail the extraction; it simply
            // contributes no evidence.
            return null;
        }
    }

    internal static bool TryParseHex(string? hex, out SKColor color)
    {
        color = default;
        var value = hex?.Trim().TrimStart('#');
        if (value is { Length: 3 })
        {
            value = string.Concat(value.Select(c => new string(c, 2)));
        }

        if (value is not { Length: 6 }
            || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
