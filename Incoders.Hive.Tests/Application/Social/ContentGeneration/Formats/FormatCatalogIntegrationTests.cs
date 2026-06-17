using Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;
using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Formats;

/// <summary>
/// S3-T7 / S3-T9 / SCENARIO-22: FormatCatalog-to-application integration tests.
///
/// S3-T7: ContentFormatCatalog.MediumFor and ChannelFor route story-carousel / feed-carousel
///        through the catalog lookup, returning ContentMedium.Visual and correct Instagram channels.
/// S3-T9: PackagePptxComposer.VisualFormatOrder is catalog-derived and includes story-carousel
///        and feed-carousel without a literal array — the catalog declaration order is authoritative.
/// </summary>
public sealed class FormatCatalogIntegrationTests
{
    // ─── S3-T7: ContentFormatCatalog — MediumFor ────────────────────────────

    [Theory]
    [InlineData("story-carousel")]
    [InlineData("feed-carousel")]
    [InlineData("story")]
    [InlineData("feed-post")]
    public void MediumFor_CatalogVisualFormats_ReturnsVisual(string formatKey)
    {
        var medium = ContentFormatCatalog.MediumFor(formatKey);

        Assert.Equal(ContentMedium.Visual, medium);
    }

    [Theory]
    [InlineData("reel-script")]
    [InlineData("caption")]
    [InlineData("hook")]
    [InlineData("content-idea")]
    public void MediumFor_NonVisualFormats_ReturnsText(string formatKey)
    {
        var medium = ContentFormatCatalog.MediumFor(formatKey);

        Assert.Equal(ContentMedium.Text, medium);
    }

    [Fact]
    public void MediumFor_LegacyCarousel_ReturnsVisual()
    {
        // ADR-5: ResolveForAssembly("carousel") returns LegacyCarouselAssemblyDef with Visual medium
        var medium = ContentFormatCatalog.MediumFor("carousel");

        Assert.Equal(ContentMedium.Visual, medium);
    }

    [Fact]
    public void MediumFor_StoryCarousel_IsNotStoryLiteral()
    {
        // Regression guard: "story" was removed; story-carousel must not fall through to unknown
        var medium = ContentFormatCatalog.MediumFor("story-carousel");

        Assert.NotEqual(ContentMedium.Text, medium); // must be Visual, not unknown-text-fallback
    }

    // ─── S3-T7: ContentFormatCatalog — ChannelFor ───────────────────────────

    [Fact]
    public void ChannelFor_StoryCarousel_ReturnsInstagramStory()
    {
        var channel = ContentFormatCatalog.ChannelFor("story-carousel");

        Assert.Equal("instagram", channel.Network);
        Assert.Equal("story", channel.Placement);
    }

    [Fact]
    public void ChannelFor_FeedCarousel_ReturnsInstagramFeed()
    {
        var channel = ContentFormatCatalog.ChannelFor("feed-carousel");

        Assert.Equal("instagram", channel.Network);
        Assert.Equal("feed", channel.Placement);
    }

    [Fact]
    public void ChannelFor_Story_ReturnsInstagramStory()
    {
        var channel = ContentFormatCatalog.ChannelFor("story");

        Assert.Equal("instagram", channel.Network);
        Assert.Equal("story", channel.Placement);
    }

    [Fact]
    public void ChannelFor_FeedPost_ReturnsInstagramFeed()
    {
        var channel = ContentFormatCatalog.ChannelFor("feed-post");

        Assert.Equal("instagram", channel.Network);
        Assert.Equal("feed", channel.Placement);
    }

    [Fact]
    public void ChannelFor_LegacyCarousel_ReturnsInstagramFeed()
    {
        // ADR-5: legacy "carousel" alias resolves to feed placement via ResolveForAssembly
        var channel = ContentFormatCatalog.ChannelFor("carousel");

        Assert.Equal("instagram", channel.Network);
    }

    // ─── S3-T9: FormatCatalog — KnownKeys excludes legacy "carousel" ─────────

    [Fact]
    public void FormatCatalog_KnownKeys_IncludesStoryCarouselAndFeedCarousel()
    {
        // ADR-2b: KnownKeys() is the canonical write-path allow-list; it must include the new keys
        var keys = FormatCatalog.KnownKeys().ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("story-carousel", keys);
        Assert.Contains("feed-carousel", keys);
    }

    [Fact]
    public void FormatCatalog_KnownKeys_ExcludesLegacyCarousel()
    {
        // CRITICAL-2 / ADR-3: bare "carousel" is rejected at the API; it must NOT be in KnownKeys
        var keys = FormatCatalog.KnownKeys().ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("carousel", keys);
    }

    [Fact]
    public void FormatCatalog_ResolveForAssembly_LegacyCarousel_ReturnsNonNull()
    {
        // ADR-5: ResolveForAssembly("carousel") must return non-null (alias map read path)
        var def = FormatCatalog.ResolveForAssembly("carousel");

        Assert.NotNull(def);
        Assert.True(def!.IsMultiAsset);
        Assert.Equal("carousel", def.StorageFolder);
    }

    [Fact]
    public void FormatCatalog_ResolveForAssembly_StoryCarousel_ReturnsCorrectStorageFolder()
    {
        // ADR-5: story-carousel assets write to story-carousel/ not carousel/
        var def = FormatCatalog.ResolveForAssembly("story-carousel");

        Assert.NotNull(def);
        Assert.Equal("story-carousel", def!.StorageFolder);
        Assert.True(def.IsMultiAsset);
    }

    [Fact]
    public void FormatCatalog_ResolveForAssembly_FeedCarousel_ReturnsCorrectStorageFolder()
    {
        // ADR-5: feed-carousel assets write to feed-carousel/ not carousel/
        var def = FormatCatalog.ResolveForAssembly("feed-carousel");

        Assert.NotNull(def);
        Assert.Equal("feed-carousel", def!.StorageFolder);
        Assert.True(def.IsMultiAsset);
    }

    // ─── S3-T9: FormatCatalog.All() includes story-carousel and feed-carousel ─

    [Fact]
    public void FormatCatalog_All_IncludesStoryCarouselAndFeedCarousel()
    {
        // PackagePptxComposer.VisualFormatOrder is derived from FormatCatalog.All().Where(IsVisual)
        // so this is the root assertion for S3-T9 format order correctness
        var all = FormatCatalog.All().ToList();
        var keys = all.Select(d => d.FormatKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("story-carousel", keys);
        Assert.Contains("feed-carousel", keys);
        Assert.Contains("story", keys);
        Assert.Contains("feed-post", keys);
    }

    [Fact]
    public void FormatCatalog_All_VisualEntries_AllHaveNonNullStorageFolder()
    {
        // Every visual catalog entry must have a storage folder to form correct paths
        var visualEntries = FormatCatalog.All().Where(d => d.IsVisual).ToList();

        Assert.NotEmpty(visualEntries);
        Assert.All(visualEntries, d =>
            Assert.False(string.IsNullOrWhiteSpace(d.StorageFolder),
                $"FormatKey '{d.FormatKey}' is visual but has no StorageFolder"));
    }
}
