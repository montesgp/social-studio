using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-7 TDD tests — prompt-version bump assertions (CG-CON-04).
///
/// Written RED before bumping ContentGenerationPromptVersions.
/// Turned GREEN by changing Carousel = "carousel.v3", Story = "story-single.v2",
/// FeedPost = "feed-single.v2".
///
/// Untouched formats must retain their current versions (regression guard).
/// </summary>
public sealed class PromptVersionBumpTests
{
    // ─── Bumped formats ───────────────────────────────────────────────────────

    [Fact]
    public void ContentGenerationPromptVersions_Carousel_IsV3()
    {
        // Latest: in-call post caption — mandatory publicationDescription + hashtags[].
        Assert.Equal("carousel.v7", ContentGenerationPromptVersions.Carousel);
    }

    [Fact]
    public void ContentGenerationPromptVersions_Story_IsV2()
    {
        // Latest: in-call post caption — mandatory publicationDescription + hashtags[].
        Assert.Equal("story-single.v5", ContentGenerationPromptVersions.Story);
    }

    [Fact]
    public void ContentGenerationPromptVersions_FeedPost_IsV2()
    {
        // Latest: in-call post caption — mandatory publicationDescription + hashtags[].
        Assert.Equal("feed-single.v6", ContentGenerationPromptVersions.FeedPost);
    }

    // ─── Untouched formats must not change ────────────────────────────────────

    [Fact]
    public void ContentGenerationPromptVersions_ReelScript_Unchanged()
    {
        Assert.Equal("reel-script.v2", ContentGenerationPromptVersions.ReelScript);
    }

    [Fact]
    public void ContentGenerationPromptVersions_ContentIdea_Unchanged()
    {
        Assert.Equal("content-idea.v2", ContentGenerationPromptVersions.ContentIdea);
    }

    [Fact]
    public void ContentGenerationPromptVersions_CtaVariation_Unchanged()
    {
        Assert.Equal("cta-variation.v2", ContentGenerationPromptVersions.CtaVariation);
    }

    [Fact]
    public void ContentGenerationPromptVersions_Caption_Unchanged()
    {
        Assert.Equal("caption.v2", ContentGenerationPromptVersions.Caption);
    }

    [Fact]
    public void ContentGenerationPromptVersions_Hook_Unchanged()
    {
        Assert.Equal("hook.v2", ContentGenerationPromptVersions.Hook);
    }
}
