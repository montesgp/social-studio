using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-6a (CAGH-PA-01/02): defensive parsing for <see cref="BrandMarkDetection.MarkType"/> and the
/// additive <see cref="BrandMarkDetection.Text"/> field. Pure record-level assertions — no
/// extractor/gateway machinery involved (that lives in <c>MultimodalAestheticStyleExtractorTests</c>).
/// </summary>
public sealed class BrandMarkDetectionTests
{
    // CAGH-PA-01-a — a recognised markType token parses through unchanged (case-insensitive).
    [Theory]
    [InlineData("text", "text")]
    [InlineData("TEXT", "text")]
    [InlineData("Text", "text")]
    [InlineData("icon", "icon")]
    [InlineData("ICON", "icon")]
    [InlineData(" icon ", "icon")]
    public void NormalizeMarkType_RecognisedToken_ParsesCaseInsensitively(string raw, string expected)
    {
        Assert.Equal(expected, BrandMarkDetection.NormalizeMarkType(raw));
    }

    // CAGH-PA-01-b — unrecognised/malformed tokens map to "unknown", never throw.
    [Theory]
    [InlineData("logo")]
    [InlineData("symbol")]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeMarkType_UnrecognisedToken_MapsToUnknown_NoThrow(string raw)
    {
        Assert.Equal("unknown", BrandMarkDetection.NormalizeMarkType(raw));
    }

    // CAGH-PA-01-b (null case) — a missing token also maps to "unknown", never throws.
    [Fact]
    public void NormalizeMarkType_Null_MapsToUnknown_NoThrow()
    {
        Assert.Equal("unknown", BrandMarkDetection.NormalizeMarkType(null));
    }

    // CAGH-PA-01-c — a pre-change profile (no markType key) deserializes with MarkType = null; this
    // is a constructor default, not something NormalizeMarkType is involved in — asserted directly
    // against the record's default parameter.
    [Fact]
    public void Constructor_DefaultMarkType_IsNull()
    {
        var brandMark = new BrandMarkDetection(Present: true, PositionHint: "bottom-right", ColorHex: "#112233");

        Assert.Null(brandMark.MarkType);
        Assert.Null(brandMark.Text);
    }

    // CAGH-PA-02-a — a text mark carries its literal signature string.
    [Fact]
    public void TextMark_CarriesLiteralText()
    {
        var brandMark = new BrandMarkDetection(
            Present: true,
            MarkType: "text",
            Text: "Dra. Lucia Fernandez");

        Assert.Equal("text", brandMark.MarkType);
        Assert.Equal("Dra. Lucia Fernandez", brandMark.Text);
    }

    // CAGH-PA-02-b — an icon mark carries no text.
    [Fact]
    public void IconMark_TextIsNull()
    {
        var brandMark = new BrandMarkDetection(Present: true, MarkType: "icon", Text: null);

        Assert.Equal("icon", brandMark.MarkType);
        Assert.Null(brandMark.Text);
    }

    // ── WU-6b (CAGH-PA-04): TryValidateBoundingBox ─────────────────────────

    // CAGH-PA-04-a — a valid normalised box within bounds and above the minimum-area guard is retained.
    [Fact]
    public void TryValidateBoundingBox_ValidBox_IsRetained()
    {
        var box = BrandMarkDetection.TryValidateBoundingBox(0.1, 0.2, 0.3, 0.3);

        Assert.NotNull(box);
        Assert.Equal(0.1, box!.X, precision: 10);
        Assert.Equal(0.2, box.Y, precision: 10);
        Assert.Equal(0.3, box.Width, precision: 10);
        Assert.Equal(0.3, box.Height, precision: 10);
    }

    // CAGH-PA-04-b — a box below the minimum-area guard (3%×3% = 0.0009) is rejected.
    [Fact]
    public void TryValidateBoundingBox_BelowMinimumArea_ReturnsNull()
    {
        var box = BrandMarkDetection.TryValidateBoundingBox(0.1, 0.1, 0.01, 0.01);

        Assert.Null(box);
    }

    // CAGH-PA-04-c — malformed/out-of-range shapes are rejected, never throw.
    [Theory]
    [InlineData(-0.1, 0.2, 0.3, 0.3)]  // negative X
    [InlineData(0.1, -0.2, 0.3, 0.3)]  // negative Y
    [InlineData(1.1, 0.2, 0.3, 0.3)]   // X > 1.0
    [InlineData(0.1, 1.2, 0.3, 0.3)]   // Y > 1.0
    [InlineData(0.8, 0.2, 0.3, 0.3)]   // X + Width > 1.0
    [InlineData(0.1, 0.8, 0.3, 0.3)]   // Y + Height > 1.0
    [InlineData(0.1, 0.2, -0.3, 0.3)]  // inverted (negative Width)
    [InlineData(0.1, 0.2, 0.3, -0.3)]  // inverted (negative Height)
    [InlineData(0.1, 0.2, 0.0, 0.3)]   // zero-area (Width = 0)
    [InlineData(0.1, 0.2, 0.3, 0.0)]   // zero-area (Height = 0)
    public void TryValidateBoundingBox_MalformedOrOutOfRange_ReturnsNull_NoThrow(
        double x, double y, double width, double height)
    {
        var box = BrandMarkDetection.TryValidateBoundingBox(x, y, width, height);

        Assert.Null(box);
    }

    // CAGH-PA-04-c — non-finite values (NaN/Infinity) are rejected, never throw.
    [Theory]
    [InlineData(double.NaN, 0.2, 0.3, 0.3)]
    [InlineData(0.1, double.PositiveInfinity, 0.3, 0.3)]
    [InlineData(0.1, 0.2, double.NegativeInfinity, 0.3)]
    public void TryValidateBoundingBox_NonFiniteValues_ReturnsNull_NoThrow(
        double x, double y, double width, double height)
    {
        var box = BrandMarkDetection.TryValidateBoundingBox(x, y, width, height);

        Assert.Null(box);
    }

    // Missing (null) coordinates never throw and never fabricate a box.
    [Fact]
    public void TryValidateBoundingBox_MissingCoordinate_ReturnsNull_NoThrow()
    {
        Assert.Null(BrandMarkDetection.TryValidateBoundingBox(null, 0.2, 0.3, 0.3));
        Assert.Null(BrandMarkDetection.TryValidateBoundingBox(0.1, null, 0.3, 0.3));
        Assert.Null(BrandMarkDetection.TryValidateBoundingBox(0.1, 0.2, null, 0.3));
        Assert.Null(BrandMarkDetection.TryValidateBoundingBox(0.1, 0.2, 0.3, null));
    }
}
