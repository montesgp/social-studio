using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Infrastructure.Social.ContentGeneration;
using SkiaSharp;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// The production image guardrail must reject DEGENERATE generated backgrounds (all-black or a single
/// flat color) so the background service retries and, on exhaustion, degrades the slide to CSS — instead
/// of shipping a black slide (observed: ComfyUI output 00031). It must stay conservative: any real photo,
/// even a dark one, passes.
/// </summary>
public sealed class DegenerateImageGuardrailValidatorTests
{
    private static ImageGenerationResult Png(SKColor fill, Action<SKCanvas>? paint = null, int size = 64)
    {
        using var bitmap = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(fill);
            paint?.Invoke(canvas);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ImageGenerationResult(data.ToArray(), "image/png", "comfyui", "test-model");
    }

    [Fact]
    public void Validate_AllBlackImage_Fails()
    {
        var validator = new DegenerateImageGuardrailValidator();

        var result = validator.Validate(Png(SKColors.Black));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_FlatSingleColorImage_Fails()
    {
        var validator = new DegenerateImageGuardrailValidator();

        // A uniform non-black color is also a generation failure for a Photo slide.
        var result = validator.Validate(Png(new SKColor(140, 106, 69)));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_NormalVariedImage_Passes()
    {
        var validator = new DegenerateImageGuardrailValidator();

        // A varied frame: dark base with lighter shapes — a plausible real background.
        var result = validator.Validate(Png(SKColors.Black, canvas =>
        {
            using var paint = new SKPaint { Color = new SKColor(220, 200, 170) };
            canvas.DrawRect(SKRect.Create(4, 4, 40, 30), paint);
            using var paint2 = new SKPaint { Color = new SKColor(120, 90, 60) };
            canvas.DrawCircle(48, 48, 12, paint2);
        }));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_DarkButDetailedImage_Passes()
    {
        var validator = new DegenerateImageGuardrailValidator();

        // Mostly dark, but with a small bright highlight (~a few % of pixels): must NOT be rejected.
        var result = validator.Validate(Png(new SKColor(8, 8, 10), canvas =>
        {
            using var paint = new SKPaint { Color = new SKColor(240, 235, 220) };
            canvas.DrawRect(SKRect.Create(24, 24, 12, 12), paint);
        }));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_UndecodableBytes_PassesFailOpen()
    {
        var validator = new DegenerateImageGuardrailValidator();

        // Never over-reject: bytes we cannot decode are passed through (fail-open), not failed.
        var result = validator.Validate(new ImageGenerationResult([0x00, 0x01, 0x02, 0x03], "image/png", "comfyui", "m"));

        Assert.True(result.IsSuccess);
    }
}
