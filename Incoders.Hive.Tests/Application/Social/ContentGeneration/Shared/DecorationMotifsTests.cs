using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 (CAVS-PA-01/02/03): pure record-level assertions for <see cref="DecorationMotifs"/>,
/// <see cref="DividerRuleStyle"/>, and <see cref="CompositionArchetypes"/>. No extractor/gateway
/// machinery involved (that lives in <c>MultimodalAestheticStyleExtractorTests</c>).
/// </summary>
public sealed class DecorationMotifsTests
{
    // CAVS-PA-02-b — an unrecognised (invented) archetype token is preserved verbatim after
    // trim/lowercase normalisation — never collapsed to "unknown" (unlike NormalizeMarkType).
    [Theory]
    [InlineData("diagonal-split", "diagonal-split")]
    [InlineData("Diagonal-Split", "diagonal-split")]
    [InlineData("  DIAGONAL-SPLIT  ", "diagonal-split")]
    public void Normalize_UnrecognisedToken_PreservedVerbatim(string raw, string expected)
    {
        Assert.Equal(expected, CompositionArchetypes.Normalize(raw));
        Assert.False(CompositionArchetypes.IsRecognised(raw));
    }

    // CAVS-PA-02-a — recognised tokens normalise case-insensitively to themselves.
    [Theory]
    [InlineData("left-aligned", "left-aligned")]
    [InlineData("LEFT-ALIGNED", "left-aligned")]
    [InlineData("centered", "centered")]
    [InlineData("Centered", "centered")]
    [InlineData("lower-third", "lower-third")]
    [InlineData("LOWER-THIRD", "lower-third")]
    public void Normalize_RecognisedToken_ParsesCaseInsensitively(string raw, string expected)
    {
        Assert.Equal(expected, CompositionArchetypes.Normalize(raw));
        Assert.True(CompositionArchetypes.IsRecognised(raw));
    }

    // CAVS-PA-02-c — null/blank archetype normalises to null.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_NullOrBlank_ReturnsNull(string? raw)
    {
        Assert.Null(CompositionArchetypes.Normalize(raw));
        Assert.False(CompositionArchetypes.IsRecognised(raw));
    }

    // CAVS-PA-03-a — a recognised divider-style token parses to its enum value, case-insensitively.
    [Theory]
    [InlineData("line", DividerRuleStyle.Line)]
    [InlineData("LINE", DividerRuleStyle.Line)]
    [InlineData("line-with-diamond", DividerRuleStyle.LineWithDiamond)]
    [InlineData("LINE-WITH-DIAMOND", DividerRuleStyle.LineWithDiamond)]
    [InlineData("line-with-dot", DividerRuleStyle.LineWithDot)]
    [InlineData("LINE-WITH-DOT", DividerRuleStyle.LineWithDot)]
    public void TryParseDividerRule_RecognisedToken_ParsesCaseInsensitively(string raw, DividerRuleStyle expected)
    {
        Assert.Equal(expected, CompositionArchetypes.TryParseDividerRule(raw));
    }

    // CAVS-PA-03-b — an unrecognised divider-style token maps to null, never throws.
    [Theory]
    [InlineData("dashed")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseDividerRule_UnrecognisedToken_ReturnsNull_NoThrow(string? raw)
    {
        Assert.Null(CompositionArchetypes.TryParseDividerRule(raw));
    }

    // CAVS-PA-01-a/c — DecorationMotifs defaults to all-null members; both members can populate
    // independently, and no AccentShape member exists anywhere on the record (compile-time absence).
    [Fact]
    public void Constructor_DefaultMembers_AreNull()
    {
        var motifs = new DecorationMotifs();

        Assert.Null(motifs.DividerRule);
        Assert.Null(motifs.CompositionArchetype);
        Assert.Null(motifs.Ornament);
    }

    [Fact]
    public void Constructor_BothMembers_PopulateIndependently()
    {
        var motifs = new DecorationMotifs(
            DividerRule: DividerRuleStyle.LineWithDot,
            CompositionArchetype: "centered");

        Assert.Equal(DividerRuleStyle.LineWithDot, motifs.DividerRule);
        Assert.Equal("centered", motifs.CompositionArchetype);
    }

    // ── CAVS-PA-04: DecorativeOrnamentDetection reuses BrandMarkBoundingBox verbatim ──────────

    // CAVS-PA-04-a — a valid ornament bounding box is retained with its resolved source path.
    [Fact]
    public void DecorativeOrnamentDetection_ValidBox_IsRetained()
    {
        var box = BrandMarkDetection.TryValidateBoundingBox(0.1, 0.2, 0.3, 0.3);
        var ornament = new DecorativeOrnamentDetection(SourceImagePath: "posts/123/img.jpg", BoundingBox: box);

        Assert.NotNull(ornament.BoundingBox);
        Assert.Equal("posts/123/img.jpg", ornament.SourceImagePath);
    }

    // CAVS-PA-04-b — below-minimum-area/out-of-range/malformed boxes never reach the record;
    // TryValidateBoundingBox already rejects them, so the ornament's BoundingBox stays null.
    [Fact]
    public void DecorativeOrnamentDetection_InvalidBox_BoundingBoxIsNull_NoThrow()
    {
        var box = BrandMarkDetection.TryValidateBoundingBox(0.1, 0.1, 0.01, 0.01);
        var ornament = new DecorativeOrnamentDetection(SourceImagePath: "posts/123/img.jpg", BoundingBox: box);

        Assert.Null(ornament.BoundingBox);
    }
}
