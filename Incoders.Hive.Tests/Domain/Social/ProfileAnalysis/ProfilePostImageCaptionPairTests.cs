using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Domain.Social.ProfileAnalysis;

/// <summary>
/// WU-1 task 1.7 [RED] — <see cref="ProfilePostImageCaptionPair"/> Domain record shape.
/// Asserts: construction compiles; Caption = null is valid (image-only scenario);
/// no Infrastructure types referenced; ProfilePostImageRef.SourcePostId confirmed as string.
/// Refs: design §3.2; tasks 1.7/1.8; CACD-PA-03-b/d.
/// </summary>
public sealed class ProfilePostImageCaptionPairTests
{
    private static readonly ProfilePostImageRef AnyImageRef =
        new(SourcePostId: "P1", AssetIndex: 0, StoragePath: "org/x/p1-0.jpg", ContentHash: "sha256:abc");

    // ── Construction with caption ─────────────────────────────────────────────

    [Fact]
    public void Construction_WithCaption_CompilesAndHoldsValues()
    {
        var pair = new ProfilePostImageCaptionPair(AnyImageRef, "Hello world");

        Assert.Equal(AnyImageRef, pair.Image);
        Assert.Equal("Hello world", pair.Caption);
    }

    // ── Caption = null is valid (CACD-PA-03-b/d) ─────────────────────────────

    [Fact]
    public void Construction_CaptionNull_IsValid_ImageOnlyScenario()
    {
        // Missing caption → null; never throws (CACD-PA-03-b).
        var pair = new ProfilePostImageCaptionPair(AnyImageRef, null);

        Assert.Equal(AnyImageRef, pair.Image);
        Assert.Null(pair.Caption);
    }

    // ── ProfilePostImageRef.SourcePostId is string ────────────────────────────

    [Fact]
    public void Image_SourcePostId_IsString()
    {
        var pair = new ProfilePostImageCaptionPair(AnyImageRef, null);

        Assert.IsType<string>(pair.Image.SourcePostId);
        Assert.Equal("P1", pair.Image.SourcePostId);
    }

    // ── with-expression compiles ──────────────────────────────────────────────

    [Fact]
    public void WithExpression_Caption_CompilesAndYieldsNewValue()
    {
        var original = new ProfilePostImageCaptionPair(AnyImageRef, null);
        var updated  = original with { Caption = "Updated caption" };

        Assert.Null(original.Caption);
        Assert.Equal("Updated caption", updated.Caption);
    }

    [Fact]
    public void WithExpression_Caption_NullToNull_IsValid()
    {
        var original = new ProfilePostImageCaptionPair(AnyImageRef, "some caption");
        var updated  = original with { Caption = null };

        Assert.Null(updated.Caption);
    }

    // ── Value equality ────────────────────────────────────────────────────────

    [Fact]
    public void EqualityIsByValue()
    {
        var a = new ProfilePostImageCaptionPair(AnyImageRef, "same");
        var b = new ProfilePostImageCaptionPair(AnyImageRef, "same");

        Assert.Equal(a, b);
    }

    // ── No Infrastructure types (compile-time gate) ───────────────────────────

    [Fact]
    public void NoInfrastructureTypesReferenced_CompileTimeGate()
    {
        // ProfilePostImageCaptionPair and ProfilePostImageRef are pure Domain; no SDK imports needed.
        var pair = new ProfilePostImageCaptionPair(AnyImageRef, null);
        Assert.NotNull(pair.Image);
    }
}
