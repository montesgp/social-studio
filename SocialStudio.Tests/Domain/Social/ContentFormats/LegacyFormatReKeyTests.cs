using SocialStudio.Domain.Social.ContentFormats;

namespace SocialStudio.Tests.Domain.Social.ContentFormats;

/// <summary>
/// Unit tests for the pure legacy → canonical format re-key mapping that drives
/// the one-time package reconstruction migration.
/// </summary>
public sealed class LegacyFormatReKeyTests
{
    [Theory]
    [InlineData("story", "story-single")]
    [InlineData("flyer", "story-single")]
    [InlineData("feed-post", "feed-single")]
    [InlineData("carousel", "feed-carousel")]
    [InlineData("STORY", "story-single")] // case-insensitive
    public void TryReKeyFormat_LegacyKey_MapsToCanonical(string legacy, string expected)
    {
        var ok = LegacyFormatReKey.TryReKeyFormat(legacy, out var canonical);

        Assert.True(ok);
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData("story-single")]
    [InlineData("feed-carousel")]
    [InlineData("reel-script")]
    [InlineData("caption")]
    [InlineData("unknown-thing")]
    [InlineData("")]
    public void TryReKeyFormat_NonLegacyKey_ReturnsFalseAndEchoesInput(string key)
    {
        var ok = LegacyFormatReKey.TryReKeyFormat(key, out var canonical);

        Assert.False(ok);
        Assert.Equal(key, canonical);
    }

    [Theory]
    [InlineData("story.png", "story-single", "story-single.png")]
    [InlineData("flyer.png", "story-single", "story-single.png")]
    [InlineData("feed-post.png", "feed-single", "feed-single.png")]
    [InlineData("carousel/slide_1.png", "feed-carousel", "feed-carousel/slide_1.png")]
    [InlineData("carousel/slide_12.png", "feed-carousel", "feed-carousel/slide_12.png")]
    public void ReKeyRelativePath_ProducesCanonicalLayout(string legacy, string canonicalKey, string expected)
    {
        var result = LegacyFormatReKey.ReKeyRelativePath(legacy, canonicalKey);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void LegacyKeys_ContainsAllFourLegacyKeys()
    {
        Assert.Contains("story", LegacyFormatReKey.LegacyKeys);
        Assert.Contains("flyer", LegacyFormatReKey.LegacyKeys);
        Assert.Contains("feed-post", LegacyFormatReKey.LegacyKeys);
        Assert.Contains("carousel", LegacyFormatReKey.LegacyKeys);
        Assert.DoesNotContain("story-single", LegacyFormatReKey.LegacyKeys);
    }
}
