using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>Phase 2 (CAG2-CG-05): caption voice + style descriptors are additive advisory guidance.</summary>
public sealed class AestheticGuidanceCaptionVoiceTests
{
    [Fact]
    public void IncludesCaptionVoice_AndStyleDescriptors_WhenPresent()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: DateTime.UtcNow,
            CaptionVoice: "lowercase, playful, emoji-light",
            StyleDescriptors: ["editorial", "warm film tones"]);

        var guidance = GenerationPromptContext.AestheticGuidance(profile);

        Assert.Contains("lowercase, playful, emoji-light", guidance);
        Assert.Contains("editorial", guidance);
        Assert.Contains("warm film tones", guidance);
        // Concision still wins (additive contract).
        Assert.Contains("CONCISION", guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase1Profile_NoCaptionVoiceSection_NoChange()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: "professional",
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: DateTime.UtcNow);

        var guidance = GenerationPromptContext.AestheticGuidance(profile);

        Assert.Contains("professional", guidance);
        Assert.DoesNotContain("caption/copy style", guidance, StringComparison.OrdinalIgnoreCase);
    }
}
