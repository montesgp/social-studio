using SkiaSharp;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Measures how condensed a typeface is from pixels (client-style-fidelity CSF-PA-02).
///
/// <para>
/// For the same words, a line's ink width divided by the face's x-height depends on the typeface,
/// not on size or line spacing. Measured on the client's real slide and on each candidate face, it
/// separates faces a vision model confuses: the model consistently picked a wider grotesque because
/// the strokes looked alike, while the line widths showed it overflowing where the client's text fits.
/// </para>
/// </summary>
internal static class FontCondensationMeter
{
    /// <summary>Rows denser than this share of the densest row belong to a line's x-height zone.</summary>
    private const double CoreRowShare = 0.35;

    /// <summary>
    /// Width / x-height of each text line in a cropped sample, top to bottom. Text is the bright ink
    /// on a dark ground (or the dark ink on a light one). Null when no line can be found.
    /// </summary>
    public static IReadOnlyList<double>? MeasureReference(SKBitmap crop, bool darkGround)
    {
        var width = crop.Width;
        var height = crop.Height;
        if (width < 8 || height < 8)
        {
            return null;
        }

        var luminance = new byte[width * height];
        var pixels = crop.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            luminance[i] = (byte)((0.2126 * c.Red) + (0.7152 * c.Green) + (0.0722 * c.Blue));
        }

        var threshold = Otsu(luminance);
        var ink = new bool[luminance.Length];
        for (var i = 0; i < luminance.Length; i++)
        {
            ink[i] = darkGround ? luminance[i] > threshold : luminance[i] < threshold;
        }

        var rowCounts = new int[height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (ink[(y * width) + x])
                {
                    rowCounts[y]++;
                }
            }
        }

        var peak = rowCounts.Max();
        if (peak == 0)
        {
            return null;
        }

        // Tight leading joins lines through ascenders and descenders, so lines are found by their dense
        // x-height zones rather than by blank gaps.
        var bands = new List<(int Start, int End)>();
        int? start = null;
        for (var y = 0; y <= height; y++)
        {
            var on = y < height && rowCounts[y] > CoreRowShare * peak;
            if (on && start is null)
            {
                start = y;
            }
            else if (!on && start is { } s)
            {
                bands.Add((s, y));
                start = null;
            }
        }

        if (bands.Count == 0)
        {
            return null;
        }

        var median = bands.Select(b => b.End - b.Start).OrderBy(h => h).ElementAt(bands.Count / 2);
        var merged = new List<(int Start, int End)> { bands[0] };
        foreach (var band in bands.Skip(1))
        {
            if (band.Start - merged[^1].End < 0.15 * median)
            {
                merged[^1] = (merged[^1].Start, band.End);
            }
            else
            {
                merged.Add(band);
            }
        }

        var ratios = new List<double>(merged.Count);
        foreach (var (s, e) in merged.Where(b => b.End - b.Start >= 0.4 * median))
        {
            int? left = null;
            var right = 0;
            for (var x = 0; x < width; x++)
            {
                for (var y = s; y < e; y++)
                {
                    if (ink[(y * width) + x])
                    {
                        left ??= x;
                        right = x;
                        break;
                    }
                }
            }

            if (left is { } l)
            {
                ratios.Add((right - l + 1) / (double)(e - s));
            }
        }

        return ratios.Count > 0 ? ratios : null;
    }

    /// <summary>
    /// Mean absolute log difference between the reference's per-line ratios and the face's, or null
    /// when the face cannot draw the sample faithfully (a caps-only face for mixed-case text).
    /// </summary>
    public static double? Score(IReadOnlyList<double> reference, IReadOnlyList<string> lines, byte[] faceBytes)
    {
        using var data = SKData.CreateCopy(faceBytes);
        using var typeface = SKTypeface.FromData(data);
        if (typeface is null)
        {
            return null;
        }

        using var font = new SKFont(typeface, 200);
        font.MeasureText("x", out var lowerX);
        font.MeasureText("X", out var upperX);
        if (lowerX.Height <= 0)
        {
            return null;
        }

        var hasLowercase = lines.Any(l => l.Any(char.IsLower));
        if (hasLowercase && Math.Abs(lowerX.Height - upperX.Height) < 0.02 * upperX.Height)
        {
            return null;
        }

        double total = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            font.MeasureText(lines[i], out var bounds);
            var ratio = bounds.Width / lowerX.Height;
            if (ratio <= 0 || reference[i] <= 0)
            {
                return null;
            }

            total += Math.Abs(Math.Log(reference[i] / ratio));
        }

        return total / lines.Count;
    }

    private static int Otsu(byte[] values)
    {
        var histogram = new int[256];
        foreach (var v in values)
        {
            histogram[v]++;
        }

        var total = values.Length;
        double sumAll = 0;
        for (var t = 0; t < 256; t++)
        {
            sumAll += t * (double)histogram[t];
        }

        double sumBackground = 0;
        var weightBackground = 0;
        double best = -1;
        var threshold = 128;
        for (var t = 0; t < 256; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0)
            {
                continue;
            }

            var weightForeground = total - weightBackground;
            if (weightForeground == 0)
            {
                break;
            }

            sumBackground += t * (double)histogram[t];
            var meanBackground = sumBackground / weightBackground;
            var meanForeground = (sumAll - sumBackground) / weightForeground;
            var between = (double)weightBackground * weightForeground * Math.Pow(meanBackground - meanForeground, 2);
            if (between > best)
            {
                best = between;
                threshold = t;
            }
        }

        return threshold;
    }
}
