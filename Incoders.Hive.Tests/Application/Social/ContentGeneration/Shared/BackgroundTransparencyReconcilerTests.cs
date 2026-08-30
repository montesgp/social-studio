using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-3a (task 3a.4, CAFF-PA-04): <see cref="BackgroundTransparencyReconciler"/> tolerantly index-aligns
/// the extracted per-slide transparency signal to <c>BackgroundPattern</c>'s length — pads with null,
/// truncates surplus, never throws.
/// </summary>
public sealed class BackgroundTransparencyReconcilerTests
{
    private static IReadOnlyList<SlideBackgroundKind> Pattern(int count) =>
        Enumerable.Repeat(SlideBackgroundKind.FlatColor, count).ToArray();

    private static SlideBackgroundTransparency Blended(string descriptor = "faded overlay") =>
        new(true, descriptor);

    [Fact]
    public void Align_TransparencyShorterThanPattern_PadsMissingIndicesWithNull()
    {
        // BackgroundPattern has 5 entries; transparency array has 3.
        var pattern = Pattern(5);
        IReadOnlyList<SlideBackgroundTransparency?> transparency = [Blended(), null, Blended("blurred")];

        var result = BackgroundTransparencyReconciler.Align(pattern, transparency);

        Assert.NotNull(result);
        Assert.Equal(5, result!.Count);
        Assert.Equal(transparency[0], result[0]);
        Assert.Null(result[1]);
        Assert.Equal(transparency[2], result[2]);
        Assert.Null(result[3]);
        Assert.Null(result[4]);
    }

    [Fact]
    public void Align_TransparencyLongerThanPattern_TruncatesSurplusEntries()
    {
        // BackgroundPattern has 3 entries; transparency array has 5.
        var pattern = Pattern(3);
        IReadOnlyList<SlideBackgroundTransparency?> transparency =
            [Blended(), Blended(), Blended(), Blended(), Blended()];

        var result = BackgroundTransparencyReconciler.Align(pattern, transparency);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
    }

    [Fact]
    public void Align_EqualLength_StraightforwardIndexAlignment_NoPaddingNeeded()
    {
        var pattern = Pattern(4);
        IReadOnlyList<SlideBackgroundTransparency?> transparency =
            [Blended("a"), null, Blended("b"), null];

        var result = BackgroundTransparencyReconciler.Align(pattern, transparency);

        Assert.NotNull(result);
        Assert.Equal(4, result!.Count);
        Assert.Equal(transparency, result);
    }

    [Fact]
    public void Align_EmptyPattern_ReturnsNull()
    {
        var result = BackgroundTransparencyReconciler.Align([], [Blended()]);

        Assert.Null(result);
    }

    [Fact]
    public void Align_NullPattern_ReturnsNull()
    {
        var result = BackgroundTransparencyReconciler.Align(null, [Blended()]);

        Assert.Null(result);
    }

    [Fact]
    public void Align_EmptyTransparency_ReturnsNull()
    {
        var result = BackgroundTransparencyReconciler.Align(Pattern(3), []);

        Assert.Null(result);
    }

    [Fact]
    public void Align_NullTransparency_ReturnsNull()
    {
        var result = BackgroundTransparencyReconciler.Align(Pattern(3), null);

        Assert.Null(result);
    }

    [Fact]
    public void Align_NullAndEmptyBothInputs_NeverThrows()
    {
        Assert.Null(BackgroundTransparencyReconciler.Align(null, null));
        Assert.Null(BackgroundTransparencyReconciler.Align([], []));
    }
}
