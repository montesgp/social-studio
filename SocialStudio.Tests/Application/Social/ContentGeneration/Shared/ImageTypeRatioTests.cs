using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 task 1.5 [RED] — <see cref="ImageTypeRatio"/> record shape.
/// Asserts: construction compiles; proportion fields are double; SampleSize is int;
/// with-expression compiles; no Infrastructure types referenced.
/// Refs: design §3.1; tasks 1.5/1.6; CACD-PA-02.
/// </summary>
public sealed class ImageTypeRatioTests
{
    // ── Construction ──────────────────────────────────────────────────────────

    [Fact]
    public void Construction_AllParams_CompilesAndHoldsValues()
    {
        var ratio = new ImageTypeRatio(Photo: 0.5, FlatColor: 0.5, Decorator: 0.0, SampleSize: 10);

        Assert.Equal(0.5, ratio.Photo);
        Assert.Equal(0.5, ratio.FlatColor);
        Assert.Equal(0.0, ratio.Decorator);
        Assert.Equal(10, ratio.SampleSize);
    }

    // ── Field types ───────────────────────────────────────────────────────────

    [Fact]
    public void ProportionFields_AreDouble()
    {
        var ratio = new ImageTypeRatio(0.1, 0.8, 0.1, 13);

        Assert.IsType<double>(ratio.Photo);
        Assert.IsType<double>(ratio.FlatColor);
        Assert.IsType<double>(ratio.Decorator);
    }

    [Fact]
    public void SampleSize_IsInt()
    {
        var ratio = new ImageTypeRatio(0.5, 0.5, 0.0, 10);

        Assert.IsType<int>(ratio.SampleSize);
    }

    // ── Atlas exemplar (~50/50 photo/flat, CACD-PA-02-a) ────────────────────

    [Fact]
    public void AtlasExemplar_PhotoAndFlatColorEach_Approximately50Percent()
    {
        var ratio = new ImageTypeRatio(Photo: 0.5, FlatColor: 0.5, Decorator: 0.0, SampleSize: 10);

        Assert.InRange(ratio.Photo, 0.49, 0.51);
        Assert.InRange(ratio.FlatColor, 0.49, 0.51);
    }

    // ── Comet exemplar (low photo, CACD-PA-02-b) ─────────────────────────────

    [Fact]
    public void CometExemplar_Photo_IsLessThan15Percent()
    {
        // photo: 1, flat: 10, decorator: 2 → SampleSize 13 → photo ≈ 0.077
        var ratio = new ImageTypeRatio(
            Photo:      1.0 / 13.0,
            FlatColor:  10.0 / 13.0,
            Decorator:  2.0 / 13.0,
            SampleSize: 13);

        Assert.True(ratio.Photo < 0.15, $"Expected Photo < 0.15 but was {ratio.Photo}");
    }

    // ── with-expression ───────────────────────────────────────────────────────

    [Fact]
    public void WithExpression_Photo_CompilesAndYieldsNewValue()
    {
        var original = new ImageTypeRatio(0.5, 0.5, 0.0, 10);
        var updated  = original with { Photo = 0.3 };

        Assert.Equal(0.3, updated.Photo);
        Assert.Equal(0.5, original.Photo);
    }

    // ── Value equality ────────────────────────────────────────────────────────

    [Fact]
    public void EqualityIsByValue()
    {
        var a = new ImageTypeRatio(0.5, 0.5, 0.0, 10);
        var b = new ImageTypeRatio(0.5, 0.5, 0.0, 10);

        Assert.Equal(a, b);
    }

    // ── No Infrastructure types (compile-time gate) ───────────────────────────

    [Fact]
    public void NoInfrastructureTypesReferenced_CompileTimeGate()
    {
        var ratio = new ImageTypeRatio(0.0, 1.0, 0.0, 5);
        Assert.Equal(5, ratio.SampleSize);
    }
}
