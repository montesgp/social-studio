using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SkiaSharp;

namespace SocialStudio.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Production <see cref="IImageGuardrailValidator"/> that rejects DEGENERATE generated backgrounds —
/// a near-black frame or a single flat color — so the background service retries (CAC-IMG-03) and,
/// on budget exhaustion, degrades that slide to the CSS path instead of shipping a broken background.
///
/// <para>
/// Motivation: a ComfyUI/VAE decode occasionally emits an all-black raster (observed: output 00031).
/// With NO validator registered, <see cref="ClientAestheticImageBackgroundService"/> accepted it as-is
/// and the studio rendered a black slide. This validator is intentionally CONSERVATIVE — it only fails
/// images it is confident are degenerate, so legitimate (even dark, moody) photos always pass.
/// </para>
///
/// <para>
/// Scope: this validator does NOT yet implement the foreground/background-only inspection described in
/// the port's doc (text/logo detection needs OCR/ML) — that remains a future concern. Rejecting an
/// obviously-broken frame is strictly better than the prior null (no validation at all).
/// </para>
/// </summary>
public sealed class DegenerateImageGuardrailValidator : IImageGuardrailValidator
{
    // A pixel at/below this luminance (0-255, Rec. 601) counts as "near-black".
    private const double NearBlackLuminance = 16.0;

    // Reject when at least this fraction of sampled pixels is near-black. Set tight (98.5%) so a real
    // photo — which always has some highlights/product/text area — is never rejected; only a frame that
    // is almost entirely black trips it.
    private const double NearBlackRejectFraction = 0.985;

    // Reject when the luminance standard deviation is below this — i.e. the frame is a single flat color
    // (any hue), which for a Photo slide is a generation failure. Real photos have stddev far above this.
    private const double FlatStdDevThreshold = 2.0;

    // Upper bound on sampled pixels; a stride keeps cost O(1) regardless of image size.
    private const int MaxSamples = 4096;

    private static readonly Error Degenerate =
        Error.Failure("image-guardrail.degenerate", "generated background is near-black or a single flat color");

    public Result<ImageGenerationResult> Validate(ImageGenerationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        SKBitmap? bitmap = null;
        try
        {
            bitmap = SKBitmap.Decode(result.Bytes);
        }
        catch
        {
            // Decode threw — treat as undecodable below.
        }

        // Fail-open: never over-reject. If we cannot decode/inspect the bytes, accept them — the
        // degenerate check is the only responsibility here, and a false rejection would needlessly
        // degrade a slide to CSS.
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            bitmap?.Dispose();
            return Result.Success(result);
        }

        using (bitmap)
        {
            var totalPixels = (long)bitmap.Width * bitmap.Height;
            var stride = (int)Math.Max(1, totalPixels / MaxSamples);

            var sampled = 0;
            var nearBlack = 0;
            var sum = 0.0;
            var sumSquares = 0.0;

            for (var i = 0L; i < totalPixels; i += stride)
            {
                var x = (int)(i % bitmap.Width);
                var y = (int)(i / bitmap.Width);
                var pixel = bitmap.GetPixel(x, y);

                // Rec. 601 luma.
                var luminance = (0.299 * pixel.Red) + (0.587 * pixel.Green) + (0.114 * pixel.Blue);

                sampled++;
                sum += luminance;
                sumSquares += luminance * luminance;
                if (luminance <= NearBlackLuminance)
                {
                    nearBlack++;
                }
            }

            if (sampled == 0)
            {
                return Result.Success(result);
            }

            var nearBlackFraction = (double)nearBlack / sampled;
            var mean = sum / sampled;
            var variance = Math.Max(0.0, (sumSquares / sampled) - (mean * mean));
            var stdDev = Math.Sqrt(variance);

            var isDegenerate = nearBlackFraction >= NearBlackRejectFraction || stdDev < FlatStdDevThreshold;
            return isDegenerate ? Result.Failure<ImageGenerationResult>(Degenerate) : Result.Success(result);
        }
    }
}
