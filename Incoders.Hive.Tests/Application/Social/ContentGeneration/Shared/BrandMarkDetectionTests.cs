using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

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
}
