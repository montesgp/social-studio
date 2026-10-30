using SocialStudio.Application.Features.Social.ContentGeneration.Formats.FeedPost;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Story;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Composers;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual;

public sealed class AttributionPolicyVisualComposerTests
{
    [Fact]
    public void StoryComposer_ReplacesCompetitorSignatureWithTargetClientAttribution()
    {
        var composer = new StoryVisualComposer();
        var payload = new StoryContent(
            "Headline",
            null,
            null,
            [],
            "CTA",
            null,
            "@competitor",
            null,
            null);

        var spec = composer.Compose(payload, BrandWithCompetitor(), new InferenceAccumulator());

        Assert.Equal("@target_brand", spec.CopySlots["signature"]);
        Assert.DoesNotContain("@competitor", spec.CopySlots.Values);
    }

    [Fact]
    public void FeedComposer_ReplacesCompetitorSignatureWithTargetClientNameWhenNoHandleExists()
    {
        var composer = new FeedPostVisualComposer();
        var payload = new FeedPostContent(
            "Headline",
            null,
            null,
            [],
            "CTA",
            null,
            "@competitor",
            null,
            null);

        var spec = composer.Compose(payload, BrandWithCompetitor(includeTargetHandle: false), new InferenceAccumulator());

        Assert.Equal("Target Brand", spec.CopySlots["signature"]);
        Assert.DoesNotContain("@competitor", spec.CopySlots.Values);
    }

    private static BrandSnapshot BrandWithCompetitor(bool includeTargetHandle = true)
    {
        var brand = new BrandManualSnapshot(
            ColorPalette: [],
            Logos: [],
            BackgroundTemplates: [],
            Typography: [],
            VoiceAndTone: null,
            Notes: includeTargetHandle ? "Official brand account: @target_brand" : null);

        // Issue #70: signature is opt-in per client — SignsOwnSlides must be true for
        // AttributionPolicy.ResolveSignature to emit an attribution.
        var client = new ClientProfileSnapshot("Target Brand", null, null, SignsOwnSlides: true);
        var competitor = new ClientAccountSnapshot(
            NetworkCode: "instagram",
            NetworkName: "Instagram",
            ProfileUrl: "https://instagram.com/competitor",
            Handle: "@competitor",
            Label: "Competitor",
            Notes: null);

        return BrandSnapshot.FromBrandData(
            brand,
            strategy: null,
            monthlyObjective: null,
            client: client,
            competitorAccounts: [competitor]);
    }
}
