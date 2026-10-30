using SocialStudio.Domain.Social.ContentFormats;
using SocialStudio.Domain.Social.ContentStudio;

namespace SocialStudio.Tests.Domain.Social.ContentFormats;

/// <summary>
/// Validates FormatCatalog against all S1 scenarios:
/// SCENARIO-01 (catalog rows), SCENARIO-02 (TryGet valid), SCENARIO-03 (TryGet null for aliases),
/// SCENARIO-06 (channel/surface distinctness), SCENARIO-14 (BySurface/ValidLayoutsFor),
/// SCENARIO-15 (Script Layout null), and the hexagonal guard for SCENARIO-17.
/// </summary>
public sealed class FormatCatalogTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // SCENARIO-01 — Catalog contains exactly the 5 v1 visual/deliverable entries
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void All_ReturnsExactly5VisualDeliverables_Plus4Auxiliaries()
    {
        var all = FormatCatalog.All();

        var deliverables = all.Where(d => !d.IsAuxiliary).ToList();
        var auxiliaries  = all.Where(d =>  d.IsAuxiliary).ToList();

        Assert.Equal(5, deliverables.Count);
        Assert.Equal(4, auxiliaries.Count);
    }

    [Theory]
    [InlineData("reel-script",    Surface.Script, null,           Medium.Text,  false, "script")]
    [InlineData("story-single",          Surface.Story,  Layout.Single,  Medium.Image, false, "story-single")]
    [InlineData("story-carousel", Surface.Story,  Layout.Carousel,Medium.Image, true,  "story-carousel")]
    [InlineData("feed-single",      Surface.Feed,   Layout.Single,  Medium.Image, false, "feed-single")]
    [InlineData("feed-carousel",  Surface.Feed,   Layout.Carousel,Medium.Image, true,  "feed-carousel")]
    public void All_EachDeliverable_HasCorrectAxesAndFolder(
        string  key, Surface surface, Layout? layout, Medium medium, bool isMultiAsset, string folder)
    {
        var def = FormatCatalog.All().Single(d => d.FormatKey == key);

        Assert.Equal(surface,     def.Surface);
        Assert.Equal(layout,      def.Layout);
        Assert.Equal(medium,      def.Medium);
        Assert.Equal(isMultiAsset,def.IsMultiAsset);
        Assert.Equal(folder,      def.StorageFolder);
        Assert.False(def.IsAuxiliary);
    }

    [Theory]
    [InlineData("reel-script",    "instagram", "reel",   ContentMedium.Text)]
    [InlineData("story-single",   "instagram", "story",  ContentMedium.Visual)]
    [InlineData("story-carousel", "instagram", "story",  ContentMedium.Visual)]
    [InlineData("feed-single",      "instagram", "feed",   ContentMedium.Visual)]
    [InlineData("feed-carousel",  "instagram", "feed",   ContentMedium.Visual)]
    public void All_EachDeliverable_HasCorrectChannelAndContentMedium(
        string key, string network, string placement, ContentMedium contentMedium)
    {
        var def = FormatCatalog.All().Single(d => d.FormatKey == key);

        Assert.Equal(network,       def.Channel.Network);
        Assert.Equal(placement,     def.Channel.Placement);
        Assert.Equal(contentMedium, def.ContentMedium);
    }

    [Theory]
    [InlineData("content-idea")]
    [InlineData("cta-variation")]
    [InlineData("caption")]
    [InlineData("hook")]
    public void All_AuxiliaryFormats_AreMarkedIsAuxiliary(string key)
    {
        var def = FormatCatalog.All().Single(d => d.FormatKey == key);

        Assert.True(def.IsAuxiliary);
        Assert.Equal(Medium.Text,      def.Medium);
        Assert.Equal(ContentMedium.Text, def.ContentMedium);
        Assert.False(def.IsMultiAsset);
        Assert.Equal(key, def.StorageFolder);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SCENARIO-02 — TryGet returns non-null for every canonical key
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("reel-script")]
    [InlineData("story-single")]
    [InlineData("story-carousel")]
    [InlineData("feed-single")]
    [InlineData("feed-carousel")]
    public void TryGet_ReturnsDefinition_ForEachCanonicalDeliverableKey(string key)
    {
        var def = FormatCatalog.TryGet(key);

        Assert.NotNull(def);
        Assert.Equal(key, def.FormatKey);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SCENARIO-03 — TryGet returns null for aliases, invalid, and legacy keys
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("story")]
    [InlineData("feed-post")]
    [InlineData("carousel")]
    [InlineData("feed-text")]
    [InlineData("story:single:image")]
    [InlineData("unknown-key")]
    [InlineData("")]
    public void TryGet_ReturnsNull_ForLegacyAliasAndInvalidKeys(string key)
    {
        var result = FormatCatalog.TryGet(key);

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ResolveForAssembly — alias-aware read-path lookup
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveForAssembly_LegacyCarousel_ReturnsNonNull()
    {
        var result = FormatCatalog.ResolveForAssembly("carousel");

        Assert.NotNull(result);
        Assert.True(result.IsMultiAsset);
        Assert.Equal("carousel", result.StorageFolder);
    }

    [Theory]
    [InlineData("story", "story")]
    [InlineData("feed-post", "feed-post")]
    public void ResolveForAssembly_LegacySingleKeys_ReturnReadOnlyDefinitions(string key, string folder)
    {
        var result = FormatCatalog.ResolveForAssembly(key);

        Assert.NotNull(result);
        Assert.False(result.IsMultiAsset);
        Assert.Equal(folder, result.StorageFolder);
    }

    [Theory]
    [InlineData("story-single")]
    [InlineData("feed-single")]
    [InlineData("reel-script")]
    [InlineData("story-carousel")]
    [InlineData("feed-carousel")]
    public void ResolveForAssembly_CanonicalKeys_PassThrough(string key)
    {
        var result = FormatCatalog.ResolveForAssembly(key);

        Assert.NotNull(result);
        Assert.Equal(key, result.FormatKey);
    }

    [Fact]
    public void ResolveForAssembly_UnknownKey_ReturnsNull()
    {
        var result = FormatCatalog.ResolveForAssembly("unknown-xyz");

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SCENARIO-06 — story-carousel and feed-carousel have distinct channels
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGet_StoryCarousel_And_FeedCarousel_HaveDistinctChannels()
    {
        var storyCarousel = FormatCatalog.TryGet("story-carousel")!;
        var feedCarousel  = FormatCatalog.TryGet("feed-carousel")!;

        Assert.NotEqual(storyCarousel.Channel, feedCarousel.Channel);
        Assert.Equal(Surface.Story, storyCarousel.Surface);
        Assert.Equal(Surface.Feed,  feedCarousel.Surface);
        Assert.Equal(Layout.Carousel, storyCarousel.Layout);
        Assert.Equal(Layout.Carousel, feedCarousel.Layout);
        Assert.Equal(Medium.Image, storyCarousel.Medium);
        Assert.Equal(Medium.Image, feedCarousel.Medium);
        Assert.True(storyCarousel.IsMultiAsset);
        Assert.True(feedCarousel.IsMultiAsset);
    }

    [Fact]
    public void TryGet_StorySingle_IsNotMultiAsset_FeedCarousel_IsMultiAsset()
    {
        var story        = FormatCatalog.TryGet("story-single")!;
        var storyCarousel = FormatCatalog.TryGet("story-carousel")!;
        var feedCarousel  = FormatCatalog.TryGet("feed-carousel")!;

        Assert.False(story.IsMultiAsset);
        Assert.True(storyCarousel.IsMultiAsset);
        Assert.True(feedCarousel.IsMultiAsset);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SCENARIO-14 — BySurface returns correct subsets (excludes auxiliaries)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BySurface_Story_ReturnsExactly_StorySingle_And_StoryCarousel()
    {
        var result = FormatCatalog.BySurface(Surface.Story).Select(d => d.FormatKey).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains("story-single",          result);
        Assert.Contains("story-carousel", result);
    }

    [Fact]
    public void BySurface_Feed_ReturnsExactly_FeedSingle_And_FeedCarousel()
    {
        var result = FormatCatalog.BySurface(Surface.Feed).Select(d => d.FormatKey).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains("feed-single",     result);
        Assert.Contains("feed-carousel", result);
    }

    [Fact]
    public void BySurface_Script_ReturnsExactly_ReelScript()
    {
        var result = FormatCatalog.BySurface(Surface.Script).Select(d => d.FormatKey).ToList();

        Assert.Single(result);
        Assert.Contains("reel-script", result);
    }

    [Fact]
    public void BySurface_ExcludesAuxiliaryFormats()
    {
        var auxiliaryKeys = new[] { "content-idea", "cta-variation", "caption", "hook" };

        foreach (var surface in Enum.GetValues<Surface>())
        {
            var keys = FormatCatalog.BySurface(surface).Select(d => d.FormatKey).ToList();
            foreach (var auxKey in auxiliaryKeys)
                Assert.DoesNotContain(auxKey, keys);
        }
    }

    [Fact]
    public void ValidLayoutsFor_Script_ReturnsEmpty()
    {
        var layouts = FormatCatalog.ValidLayoutsFor(Surface.Script);

        Assert.Empty(layouts);
    }

    [Fact]
    public void ValidLayoutsFor_Story_Returns_Single_And_Carousel()
    {
        var layouts = FormatCatalog.ValidLayoutsFor(Surface.Story).ToList();

        Assert.Equal(2, layouts.Count);
        Assert.Contains(Layout.Single,   layouts);
        Assert.Contains(Layout.Carousel, layouts);
    }

    [Fact]
    public void ValidLayoutsFor_Feed_Returns_Single_And_Carousel()
    {
        var layouts = FormatCatalog.ValidLayoutsFor(Surface.Feed).ToList();

        Assert.Equal(2, layouts.Count);
        Assert.Contains(Layout.Single,   layouts);
        Assert.Contains(Layout.Carousel, layouts);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SCENARIO-15 — reel-script FormatDefinition has null Layout
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGet_ReelScript_HasNullLayout()
    {
        var def = FormatCatalog.TryGet("reel-script")!;

        Assert.Null(def.Layout);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // KnownKeys — catalog-derived selectable whitelist
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("reel-script")]
    [InlineData("story-single")]
    [InlineData("story-carousel")]
    [InlineData("feed-single")]
    [InlineData("feed-carousel")]
    [InlineData("content-idea")]
    [InlineData("cta-variation")]
    [InlineData("caption")]
    [InlineData("hook")]
    public void KnownKeys_ContainsAllCanonicalAndAuxiliaryKeys(string key)
    {
        Assert.Contains(key, FormatCatalog.KnownKeys());
    }

    [Fact]
    public void KnownKeys_DoesNotContain_LegacyCarousel()
    {
        Assert.DoesNotContain("carousel", FormatCatalog.KnownKeys());
    }

    [Fact]
    public void KnownKeys_IsDerivedFrom_All()
    {
        var fromAll = FormatCatalog.All().Select(d => d.FormatKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known   = FormatCatalog.KnownKeys();

        // Every key in KnownKeys must come from All()
        foreach (var key in known)
            Assert.Contains(key, fromAll);
    }
}
