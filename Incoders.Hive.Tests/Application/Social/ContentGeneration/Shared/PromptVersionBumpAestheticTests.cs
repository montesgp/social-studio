using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// TDD snapshot tests for prompt version bumps after WU-4 aesthetic guidance was added
/// to Carousel, Story, and FeedPost generators (CAG-CG-11).
///
/// These tests pin the version constants AFTER the bump. They will fail (RED) until
/// ContentGenerationPromptVersions constants are incremented.
///
/// Refs: CAG-CG-11; tasks 4.9–4.10.
/// </summary>
public sealed class PromptVersionBumpAestheticTests
{
    // Version strings before WU-4 (baseline values from ContentGenerationPromptVersions before bump):
    // Carousel was "carousel.v3" → must be "carousel.v4"
    // Story was "story-single.v2" → must be "story-single.v3"
    // FeedPost was "feed-single.v2" → must be "feed-single.v3"

    [Fact]
    public void CarouselPromptVersion_BumpedAfterAestheticGuidanceAdded()
    {
        // Bumped through: aesthetic guidance (v4), conditional CTA (v5), client-directive precedence (v6).
        Assert.Equal("carousel.v6", ContentGenerationPromptVersions.Carousel);
    }

    [Fact]
    public void StoryPromptVersion_BumpedAfterAestheticGuidanceAdded()
    {
        // Bumped through: aesthetic guidance (v3), client-directive precedence + CTA/signature rules (v4).
        Assert.Equal("story-single.v4", ContentGenerationPromptVersions.Story);
    }

    [Fact]
    public void FeedPostPromptVersion_BumpedAfterAestheticGuidanceAdded()
    {
        // Bumped through: aesthetic guidance (v3), conditional CTA (v4), client-directive precedence (v5).
        Assert.Equal("feed-single.v5", ContentGenerationPromptVersions.FeedPost);
    }

    [Fact]
    public void NonModifiedGenerators_VersionsUnchanged()
    {
        // WU-4 only touched Carousel, Story, and FeedPost.
        // ReelScript, ContentIdea, CtaVariation, Caption, Hook must be UNCHANGED.
        Assert.Equal("reel-script.v2", ContentGenerationPromptVersions.ReelScript);
        Assert.Equal("content-idea.v2", ContentGenerationPromptVersions.ContentIdea);
        Assert.Equal("cta-variation.v2", ContentGenerationPromptVersions.CtaVariation);
        Assert.Equal("caption.v2", ContentGenerationPromptVersions.Caption);
        Assert.Equal("hook.v2", ContentGenerationPromptVersions.Hook);
    }
}
