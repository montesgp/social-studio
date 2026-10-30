using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// Tests for <see cref="BackgroundPatternReconciler"/> — the analysis-time fix that keeps the persisted
/// per-slide BackgroundPattern (order) consistent with the corpus photo ratio (magnitude), so an
/// image-heavy client can never end up with an all-flat pattern that suppresses image generation.
/// </summary>
public sealed class BackgroundPatternReconcilerTests
{
    private static ImageTypeRatio Ratio(double photo, double flat, double dec, int sample) =>
        new(photo, flat, dec, sample);

    [Fact]
    public void MinPhotoRatio_IsThirtyPercent()
    {
        Assert.Equal(0.30, BackgroundPatternReconciler.MinPhotoRatio);
    }

    [Fact]
    public void Reconcile_AllFlatPattern_MeaningfulPhotoRatio_InjectsProportionalPhotoSlide()
    {
        // Real "Comet Comedy" case: pattern all-flat, photo ratio 0.357, 3 slides.
        // target = round(0.357*3 away) = 1 → inject one photo at the first slot.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor
        ];

        var result = BackgroundPatternReconciler.Reconcile(pattern, Ratio(0.357, 0.571, 0.072, 28));

        Assert.NotNull(result);
        Assert.Equal(
            [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor],
            result);
    }

    [Fact]
    public void Reconcile_AllFlatPattern_HighPhotoRatio_InjectsSpreadFromEdges()
    {
        // ratio 0.6 * 4 = 2.4 → round away = 2 → inject at first and last slots.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor
        ];

        var result = BackgroundPatternReconciler.Reconcile(pattern, Ratio(0.6, 0.4, 0.0, 20));

        Assert.Equal(
            [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor, SlideBackgroundKind.Photo],
            result!);
    }

    [Fact]
    public void Reconcile_SubThresholdRatio_LeavesPatternUnchanged()
    {
        // Genuinely flat-dominant client (photo < 0.30) → no injection.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor
        ];

        var result = BackgroundPatternReconciler.Reconcile(pattern, Ratio(0.10, 0.80, 0.10, 20));

        Assert.Same(pattern, result);
    }

    [Fact]
    public void Reconcile_NullRatio_LeavesPatternUnchanged()
    {
        IReadOnlyList<SlideBackgroundKind> pattern = [SlideBackgroundKind.FlatColor];

        var result = BackgroundPatternReconciler.Reconcile(pattern, ratio: null);

        Assert.Same(pattern, result);
    }

    [Fact]
    public void Reconcile_NullPattern_ReturnsNull()
    {
        Assert.Null(BackgroundPatternReconciler.Reconcile(null, Ratio(0.9, 0.1, 0.0, 10)));
    }

    [Fact]
    public void Reconcile_PatternAlreadyMeetsTarget_ReturnsUnchanged()
    {
        // Pattern already has one photo (slide 3); target for 0.357*3≈1 is met → no change.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        var result = BackgroundPatternReconciler.Reconcile(pattern, Ratio(0.357, 0.571, 0.072, 28));

        Assert.Same(pattern, result);
    }

    [Fact]
    public void Reconcile_NeverDropsExistingPhotoSlides_WhenToppingUp()
    {
        // Pattern has a photo at the last slot; target 2 (0.5*4 away) → add first, keep last.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        var result = BackgroundPatternReconciler.Reconcile(pattern, Ratio(0.5, 0.5, 0.0, 16));

        Assert.Equal(
            [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.FlatColor, SlideBackgroundKind.Photo],
            result!);
    }

    [Fact]
    public void Reconcile_DecoratorSlots_AreEligibleForPhotoInjection()
    {
        // First non-photo slot (a decorator) can be flipped to photo when the ratio demands it.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.Decorator,
            SlideBackgroundKind.FlatColor
        ];

        var result = BackgroundPatternReconciler.Reconcile(pattern, Ratio(0.5, 0.3, 0.2, 20));

        // target = round(0.5*2 away) = 1 → inject at first slot (the decorator).
        Assert.Equal([SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor], result!);
    }
}
