using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Social;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    private readonly string? _clientAssetRootPath;
    private readonly string _clientAssetPublicBasePath;

    public LocalSvgContentRenderer()
    {
        _clientAssetRootPath = null;
        _clientAssetPublicBasePath = "/storage/client-assets";
    }

    public LocalSvgContentRenderer(
        IOptions<ClientAssetStorageOptions> clientAssetStorageOptions,
        IHostEnvironment environment)
    {
        var options = clientAssetStorageOptions.Value;
        _clientAssetRootPath = options.ResolveRootPath(environment.ContentRootPath);
        _clientAssetPublicBasePath = options.NormalizedPublicBasePath();
    }

    public Task<Result<RenderedAsset>> RenderAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct)
    {
        try
        {
            if (spec.OutputKind == CompositionOutputKind.Text)
            {
                var body = spec.TextBody ?? string.Empty;
                var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                var asset = RenderedAsset.Create(spec.FormatKey, "text/plain; charset=utf-8", $"{spec.FormatKey}.txt", bytes);
                return Task.FromResult(Result.Success(asset));
            }

            var pngBytes = RenderToPng(spec);
            var pngAsset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", pngBytes);
            return Task.FromResult(Result.Success(pngAsset));
        }
        catch (Exception ex)
        {
            var error = RenderErrors.RenderFailed(ex.Message);
            return Task.FromResult(Result.Failure<RenderedAsset>(error));
        }
    }

    private byte[] RenderToPng(VisualCompositionSpec spec)
    {
        if (string.Equals(spec.FormatKey, "flyer", StringComparison.OrdinalIgnoreCase))
        {
            return RenderFlyerToPng(spec);
        }

        return RenderTextCardToPng(spec);
    }

    private static byte[] RenderTextCardToPng(VisualCompositionSpec spec)
    {
        int width = spec.Dimensions.Width;
        int height = spec.Dimensions.Height;

        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        var background = ParseColor(spec.Palette.Background, SKColors.White);
        var primary = ParseColor(spec.Palette.Primary, new SKColor(31, 41, 55));
        var accent = ParseColor(spec.Palette.Accent, new SKColor(59, 130, 246));

        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            [background, WithAlpha(accent, 72), WithAlpha(primary, 120)],
            [0f, 0.62f, 1f],
            SKShaderTileMode.Clamp);
        using var backgroundPaint = new SKPaint
        {
            Shader = gradient,
            IsAntialias = true,
        };
        canvas.DrawRect(0, 0, width, height, backgroundPaint);
        DrawSeededDecorativeShapes(canvas, spec, width, height, accent, primary);

        using var panelPaint = new SKPaint
        {
            Color = WithAlpha(background, 214),
            IsAntialias = true,
        };
        canvas.DrawRoundRect(SKRect.Create(40, 52, width - 80, height - 124), 42, 42, panelPaint);

        // Draw copy slots as text
        using var headingPaint = new SKPaint { IsAntialias = true };
        using var bodyPaint = new SKPaint { IsAntialias = true };

        headingPaint.Color = ParseColor(spec.Palette.Text, SKColors.Black);
        bodyPaint.Color = ParseColor(spec.Palette.Secondary, new SKColor(55, 65, 81));

        using var headingFont = new SKFont(SKTypeface.FromFamilyName(spec.Typography.HeadingFamily), spec.Typography.HeadingSize);
        using var bodyFont = new SKFont(SKTypeface.FromFamilyName(spec.Typography.BodyFamily), spec.Typography.BodySize);

        float y = 118f;
        float margin = 80f;
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

    private byte[] RenderFlyerToPng(VisualCompositionSpec spec)
    {
        int width = spec.Dimensions.Width;
        int height = spec.Dimensions.Height;

        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        DrawFlyerBackground(canvas, spec, width, height);
        DrawFlyerCopy(canvas, spec, width, height);

        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private void DrawFlyerBackground(SKCanvas canvas, VisualCompositionSpec spec, int width, int height)
    {
        var backgroundSlot = spec.AssetSlots.TryGetValue("background", out var slot) ? slot : null;
        using var backgroundBitmap = TryLoadLocalBitmap(backgroundSlot?.ImageUrl);
        if (backgroundBitmap is not null)
        {
            DrawImageCover(canvas, backgroundBitmap, SKRect.Create(0, 0, width, height));
        }
        else
        {
            DrawGeneratedFlyerBackground(canvas, spec, width, height);
        }

        var primary = ParseColor(spec.Palette.Primary, new SKColor(31, 41, 55));
        using var overlayPaint = new SKPaint
        {
            Color = WithAlpha(primary, 72),
            IsAntialias = true
        };
        canvas.DrawRect(0, 0, width, height, overlayPaint);
    }

    private static void DrawGeneratedFlyerBackground(SKCanvas canvas, VisualCompositionSpec spec, int width, int height)
    {
        var primary = ParseColor(spec.Palette.Primary, new SKColor(31, 41, 55));
        var secondary = ParseColor(spec.Palette.Secondary, new SKColor(55, 65, 81));
        var accent = ParseColor(spec.Palette.Accent, new SKColor(59, 130, 246));
        var background = ParseColor(spec.Palette.Background, new SKColor(249, 250, 251));
        var seed = ComputeVisualSeed(spec);
        var gradientEndX = width * (0.72f + SeededUnit(seed, 1) * 0.28f);
        var gradientEndY = height * (0.78f + SeededUnit(seed, 2) * 0.22f);

        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(gradientEndX, gradientEndY),
            [background, WithAlpha(primary, 225), WithAlpha(accent, 220)],
            [0f, 0.58f, 1f],
            SKShaderTileMode.Clamp);
        using var backgroundPaint = new SKPaint
        {
            Shader = gradient,
            IsAntialias = true
        };
        canvas.DrawRect(0, 0, width, height, backgroundPaint);

        using var bandPaint = new SKPaint
        {
            Color = WithAlpha(secondary, 92),
            IsAntialias = true
        };
        using var band = new SKPath();
        var bandTop = height * (0.12f + SeededUnit(seed, 3) * 0.16f);
        var bandDepth = height * (0.20f + SeededUnit(seed, 4) * 0.08f);
        band.MoveTo(-140, bandTop);
        band.LineTo(width, bandTop - height * (0.06f + SeededUnit(seed, 5) * 0.08f));
        band.LineTo(width + 160, bandTop + bandDepth);
        band.LineTo(0, bandTop + bandDepth + height * 0.08f);
        band.Close();
        canvas.DrawPath(band, bandPaint);

        DrawSeededDecorativeShapes(canvas, spec, width, height, accent, secondary);

        using var accentPaint = new SKPaint
        {
            Color = WithAlpha(accent, 150),
            IsAntialias = true
        };
        var accentX = width * (0.62f + SeededUnit(seed, 6) * 0.22f);
        var accentWidth = width * (0.10f + SeededUnit(seed, 7) * 0.10f);
        canvas.DrawRect(SKRect.Create(accentX, 0, accentWidth, height), accentPaint);

    }

    private void DrawFlyerCopy(SKCanvas canvas, VisualCompositionSpec spec, int width, int height)
    {
        float margin = 84f;
        float panelTop = height * 0.33f;
        float panelHeight = height * 0.47f;
        float panelWidth = width - margin * 2;
        var panelRect = SKRect.Create(margin, panelTop, panelWidth, panelHeight);

        using var panelPaint = new SKPaint
        {
            Color = WithAlpha(ParseColor(spec.Palette.Background, SKColors.White), 224),
            IsAntialias = true
        };
        canvas.DrawRoundRect(panelRect, 34, 34, panelPaint);

        var accent = ParseColor(spec.Palette.Accent, new SKColor(59, 130, 246));
        using var accentPaint = new SKPaint
        {
            Color = accent,
            IsAntialias = true
        };
        canvas.DrawRoundRect(
            SKRect.Create(panelRect.Left, panelRect.Top, 12, panelRect.Height),
            6,
            6,
            accentPaint);

        DrawFlyerLogo(canvas, spec, margin, 106f);

        string headline = GetCopy(spec, "headline", "Flyer Headline");
        string? subtitle = GetCopy(spec, "subtitle");
        string? body = GetCopy(spec, "body");
        string? callouts = GetCopy(spec, "callouts");
        string cta = GetCopy(spec, "cta", "Learn more");
        string? signature = GetCopy(spec, "signature");

        float contentX = panelRect.Left + 56;
        float contentMaxWidth = panelRect.Width - 112;
        float y = panelRect.Top + 92;

        using var headingFont = CreateFittedFont(
            spec.Typography.HeadingFamily,
            Math.Max(58, spec.Typography.HeadingSize * 2.35f),
            42,
            headline,
            contentMaxWidth);
        using var subtitleFont = new SKFont(SKTypeface.FromFamilyName(spec.Typography.BodyFamily), 35);
        using var bodyFont = new SKFont(SKTypeface.FromFamilyName(spec.Typography.BodyFamily), 31);
        using var ctaFont = CreateFittedFont(
            spec.Typography.HeadingFamily,
            40,
            28,
            cta,
            contentMaxWidth - 92);
        using var signatureFont = new SKFont(SKTypeface.FromFamilyName(spec.Typography.BodyFamily), 34);

        using var headingPaint = new SKPaint
        {
            Color = ParseColor(spec.Palette.Text, SKColors.Black),
            IsAntialias = true
        };
        using var bodyPaint = new SKPaint
        {
            Color = ParseColor(spec.Palette.Secondary, new SKColor(55, 65, 81)),
            IsAntialias = true
        };

        var saved = canvas.Save();
        canvas.ClipRect(SKRect.Create(
            contentX,
            panelRect.Top + 42,
            contentMaxWidth,
            panelRect.Height - 176));

        y = DrawWrappedText(canvas, headline, headingFont, headingPaint, contentX, y, contentMaxWidth, 1.08f);
        y += 22;

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            y = DrawWrappedText(canvas, subtitle, subtitleFont, bodyPaint, contentX, y, contentMaxWidth, 1.18f);
            y += 16;
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            y = DrawWrappedText(canvas, body, bodyFont, bodyPaint, contentX, y, contentMaxWidth, 1.24f);
            y += 18;
        }

        if (!string.IsNullOrWhiteSpace(callouts))
        {
            using var calloutPaint = new SKPaint
            {
                Color = ParseColor(spec.Palette.Primary, new SKColor(31, 41, 55)),
                IsAntialias = true
            };
            y = DrawWrappedText(canvas, callouts, bodyFont, calloutPaint, contentX, y, contentMaxWidth, 1.22f);
            y += 34;
        }

        canvas.RestoreToCount(saved);

        DrawFlyerCta(canvas, cta, ctaFont, spec, contentX, panelRect.Bottom - 116, contentMaxWidth);

        if (!string.IsNullOrWhiteSpace(signature))
        {
            using var signaturePaint = new SKPaint
            {
                Color = ParseColor(spec.Palette.Background, SKColors.White),
                IsAntialias = true
            };
            canvas.DrawText(signature, margin, height - 92, SKTextAlign.Left, signatureFont, signaturePaint);
        }
    }

    private void DrawFlyerLogo(SKCanvas canvas, VisualCompositionSpec spec, float x, float y)
    {
        if (!spec.AssetSlots.TryGetValue("logo", out var logoSlot))
        {
            return;
        }

        using var logoBitmap = TryLoadLocalBitmap(logoSlot.ImageUrl);
        if (logoBitmap is null)
        {
            return;
        }

        const float maxWidth = 220f;
        const float maxHeight = 110f;
        var scale = Math.Min(maxWidth / logoBitmap.Width, maxHeight / logoBitmap.Height);
        var width = logoBitmap.Width * scale;
        var height = logoBitmap.Height * scale;
        var destination = SKRect.Create(x, y, width, height);

        using var paint = new SKPaint
        {
            IsAntialias = true
        };
        canvas.DrawBitmap(logoBitmap, destination, paint);
    }

    private static void DrawFlyerCta(
        SKCanvas canvas,
        string cta,
        SKFont ctaFont,
        VisualCompositionSpec spec,
        float x,
        float y,
        float maxWidth)
    {
        var accent = ParseColor(spec.Palette.Accent, new SKColor(59, 130, 246));
        var buttonHeight = 86f;
        var textWidth = Math.Min(ctaFont.MeasureText(cta), maxWidth - 92);
        var buttonWidth = Math.Min(maxWidth, Math.Max(320, textWidth + 92));
        var buttonRect = SKRect.Create(x, y, buttonWidth, buttonHeight);

        using var buttonPaint = new SKPaint
        {
            Color = accent,
            IsAntialias = true
        };
        canvas.DrawRoundRect(buttonRect, 24, 24, buttonPaint);

        using var ctaPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        var baseline = buttonRect.MidY + ctaFont.Size * 0.35f;
        canvas.DrawText(cta, buttonRect.MidX, baseline, SKTextAlign.Center, ctaFont, ctaPaint);
    }

    private SKBitmap? TryLoadLocalBitmap(string? imageUrl)
    {
        if (!TryResolveLocalAssetPath(imageUrl, out var path))
        {
            return null;
        }

        try
        {
            return SKBitmap.Decode(path);
        }
        catch
        {
            return null;
        }
    }

    private bool TryResolveLocalAssetPath(string? imageUrl, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(_clientAssetRootPath))
        {
            return false;
        }

        var assetPath = ExtractPath(imageUrl);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        var normalizedBase = _clientAssetPublicBasePath.TrimEnd('/');
        if (!assetPath.StartsWith(normalizedBase + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = assetPath[normalizedBase.Length..].TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(_clientAssetRootPath);
        var candidate = Path.GetFullPath(Path.Combine(root, relative));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private static string? ExtractPath(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            return uri.AbsolutePath;
        }

        var queryIndex = imageUrl.IndexOfAny(['?', '#']);
        return queryIndex >= 0 ? imageUrl[..queryIndex] : imageUrl;
    }

    private static void DrawImageCover(SKCanvas canvas, SKBitmap bitmap, SKRect destination)
    {
        var sourceRatio = (float)bitmap.Width / bitmap.Height;
        var destinationRatio = destination.Width / destination.Height;
        SKRect source;

        if (sourceRatio > destinationRatio)
        {
            var sourceWidth = bitmap.Height * destinationRatio;
            var sourceLeft = (bitmap.Width - sourceWidth) / 2f;
            source = SKRect.Create(sourceLeft, 0, sourceWidth, bitmap.Height);
        }
        else
        {
            var sourceHeight = bitmap.Width / destinationRatio;
            var sourceTop = (bitmap.Height - sourceHeight) / 2f;
            source = SKRect.Create(0, sourceTop, bitmap.Width, sourceHeight);
        }

        using var paint = new SKPaint
        {
            IsAntialias = true
        };
        canvas.DrawBitmap(bitmap, source, destination, paint);
    }

    private static string GetCopy(VisualCompositionSpec spec, string key, string fallback) =>
        string.IsNullOrWhiteSpace(GetCopy(spec, key)) ? fallback : GetCopy(spec, key)!;

    private static string? GetCopy(VisualCompositionSpec spec, string key) =>
        spec.CopySlots.TryGetValue(key, out var value) ? value : null;

    private static SKFont CreateFittedFont(
        string family,
        float preferredSize,
        float minimumSize,
        string text,
        float maxWidth)
    {
        var longestWord = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .OrderByDescending(word => word.Length)
            .FirstOrDefault() ?? text;

        for (var size = preferredSize; size >= minimumSize; size -= 2)
        {
            var font = new SKFont(SKTypeface.FromFamilyName(family), size);
            if (font.MeasureText(longestWord) <= maxWidth)
            {
                return font;
            }

            font.Dispose();
        }

        return new SKFont(SKTypeface.FromFamilyName(family), minimumSize);
    }

    private static void DrawSeededDecorativeShapes(
        SKCanvas canvas,
        VisualCompositionSpec spec,
        int width,
        int height,
        SKColor accent,
        SKColor secondary)
    {
        var seed = ComputeVisualSeed(spec);
        using var accentPaint = new SKPaint
        {
            Color = WithAlpha(accent, 82),
            IsAntialias = true
        };
        using var secondaryPaint = new SKPaint
        {
            Color = WithAlpha(secondary, 64),
            IsAntialias = true
        };

        for (var i = 0; i < 4; i++)
        {
            var x = width * SeededUnit(seed, 20 + i * 3);
            var y = height * SeededUnit(seed, 21 + i * 3);
            var radius = width * (0.08f + SeededUnit(seed, 22 + i * 3) * 0.11f);
            canvas.DrawCircle(x, y, radius, i % 2 == 0 ? accentPaint : secondaryPaint);
        }
    }

    private static uint ComputeVisualSeed(VisualCompositionSpec spec)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;

            void Add(string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                foreach (var ch in value)
                {
                    hash ^= ch;
                    hash *= prime;
                }
            }

            Add(spec.FormatKey);
            Add(spec.LayoutType);
            foreach (var (key, value) in spec.CopySlots.OrderBy(slot => slot.Key, StringComparer.Ordinal))
            {
                Add(key);
                Add(value);
            }

            return hash;
        }
    }

    private static float SeededUnit(uint seed, int salt)
    {
        unchecked
        {
            var value = seed + ((uint)salt * 0x9E3779B9u);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0xFFFF) / 65535f;
        }
    }

    private static SKColor WithAlpha(SKColor color, byte alpha) =>
        new(color.Red, color.Green, color.Blue, alpha);

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
