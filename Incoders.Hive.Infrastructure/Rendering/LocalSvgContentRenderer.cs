using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using SkiaSharp;

namespace Incoders.Hive.Infrastructure.Rendering;

/// <summary>
/// PNG-only renderer (v1). No PDF. Rasterizes a <see cref="VisualCompositionSpec"/>
/// using SkiaSharp primitives directly — no external SVG library required.
/// <para>
/// Text-only formats (hook, caption, cta-variation, content-idea, reel-script) produce a
/// styled "text card" PNG. Visual formats (carousel, flyer) produce a branded background with
/// copy slots painted as text.
/// </para>
/// <para>
/// For carousel, each asset slot starting with "slide-" produces its own PNG entry at the
/// caller's discretion (this renderer always produces exactly one PNG per RenderAsync call).
/// The orchestrator calls RenderAsync once per slide when needed.
/// </para>
/// </summary>
public sealed class LocalSvgContentRenderer : IContentRenderer
{
    public Task<Result<RenderedAsset>> RenderAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct)
    {
        try
        {
            var bytes = RenderToPng(spec);
            var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
            return Task.FromResult(Result.Success(asset));
        }
        catch (Exception ex)
        {
            var error = RenderErrors.RenderFailed(ex.Message);
            return Task.FromResult(Result.Failure<RenderedAsset>(error));
        }
    }

    private static byte[] RenderToPng(VisualCompositionSpec spec)
    {
        int width = spec.Dimensions.Width;
        int height = spec.Dimensions.Height;

        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        // Background
        canvas.Clear(ParseColor(spec.Palette.Background, SKColors.White));

        // Draw decorative accent bar at top (20px)
        using var accentPaint = new SKPaint
        {
            Color = ParseColor(spec.Palette.Accent, new SKColor(59, 130, 246)),
            IsAntialias = true,
        };
        canvas.DrawRect(0, 0, width, 20, accentPaint);

        // Draw copy slots as text
        using var headingPaint = new SKPaint { IsAntialias = true };
        using var bodyPaint = new SKPaint { IsAntialias = true };

        headingPaint.Color = ParseColor(spec.Palette.Text, SKColors.Black);
        bodyPaint.Color = ParseColor(spec.Palette.Secondary, new SKColor(55, 65, 81));

        using var headingFont = new SKFont(SKTypeface.FromFamilyName(spec.Typography.HeadingFamily), spec.Typography.HeadingSize);
        using var bodyFont = new SKFont(SKTypeface.FromFamilyName(spec.Typography.BodyFamily), spec.Typography.BodySize);

        float y = 80f;
        float margin = 48f;
        float maxWidth = width - 2 * margin;

        foreach (var (slotKey, text) in spec.CopySlots)
        {
            bool isHeading = slotKey is "title" or "headline" or "text" or "script" or "idea";
            var font = isHeading ? headingFont : bodyFont;
            var paint = isHeading ? headingPaint : bodyPaint;

            y = DrawWrappedText(canvas, text, font, paint, margin, y, maxWidth, isHeading ? 1.4f : 1.2f);
            y += isHeading ? 20 : 12;

            if (y > height - 80) break;
        }

        // Draw format key label at bottom
        using var labelFont = new SKFont(SKTypeface.Default, 14);
        using var labelPaint = new SKPaint { Color = ParseColor(spec.Palette.Secondary, new SKColor(107, 114, 128)), IsAntialias = true };
        canvas.DrawText(spec.FormatKey.ToUpperInvariant(), margin, height - 30, SKTextAlign.Left, labelFont, labelPaint);

        // Primary bottom bar
        using var primaryPaint = new SKPaint
        {
            Color = ParseColor(spec.Palette.Primary, new SKColor(31, 41, 55)),
            IsAntialias = true,
        };
        canvas.DrawRect(0, height - 12, width, 12, primaryPaint);

        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Draws text with simple word-wrap. Returns the updated Y position.</summary>
    private static float DrawWrappedText(SKCanvas canvas, string text, SKFont font, SKPaint paint,
        float x, float y, float maxWidth, float lineHeightMultiplier)
    {
        if (string.IsNullOrWhiteSpace(text)) return y;

        float lineHeight = font.Size * lineHeightMultiplier;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentLine = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            var testLine = currentLine.Length == 0 ? word : $"{currentLine} {word}";
            var measured = font.MeasureText(testLine);

            if (measured > maxWidth && currentLine.Length > 0)
            {
                canvas.DrawText(currentLine.ToString(), x, y, SKTextAlign.Left, font, paint);
                y += lineHeight;
                currentLine.Clear();
                currentLine.Append(word);
            }
            else
            {
                if (currentLine.Length > 0) currentLine.Append(' ');
                currentLine.Append(word);
            }
        }

        if (currentLine.Length > 0)
        {
            canvas.DrawText(currentLine.ToString(), x, y, SKTextAlign.Left, font, paint);
            y += lineHeight;
        }

        return y;
    }

    private static SKColor ParseColor(string hex, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length < 4) return fallback;

        try
        {
            var s = hex.TrimStart('#');
            if (s.Length == 3)
            {
                s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
            }
            if (s.Length == 6)
            {
                var r = Convert.ToByte(s[..2], 16);
                var g = Convert.ToByte(s[2..4], 16);
                var b = Convert.ToByte(s[4..6], 16);
                return new SKColor(r, g, b);
            }
        }
        catch
        {
            // fall through to fallback
        }

        return fallback;
    }
}
