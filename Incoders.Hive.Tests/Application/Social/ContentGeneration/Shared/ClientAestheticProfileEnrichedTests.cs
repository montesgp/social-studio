using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>Phase 2 (CAG2-PA-03/04): enriched optional fields; palette optional; forward-compat.</summary>
public sealed class ClientAestheticProfileEnrichedTests
{
    [Fact]
    public void Phase1Construction_LeavesEnrichedFieldsNull_NoBreakingChange()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: ["soft gradients"],
            VoiceTone: "warm",
            AverageSlidesPerCarousel: 5.0,
            AnalyzedAtUtc: DateTime.UtcNow);

        Assert.Null(profile.Palette);
        Assert.Null(profile.StyleDescriptors);
        Assert.Null(profile.Motifs);
        Assert.Null(profile.CaptionVoice);
        Assert.Null(profile.ReferenceImages);
    }

    [Fact]
    public void EmptyPalette_IsValidState_NotADegrade()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: DateTime.UtcNow,
            Palette: [],
            StyleDescriptors: ["editorial, high-contrast", "lots of negative space"],
            Motifs: ["hand-drawn arrows"],
            CaptionVoice: "short, lowercase, playful",
            ReferenceImages: [new ProfilePostImageRef("p1", 0, "path", "hash")]);

        // Empty palette must coexist with rich creatives-first signal.
        Assert.NotNull(profile.Palette);
        Assert.Empty(profile.Palette!);
        Assert.Single(profile.ReferenceImages!);
        Assert.Equal(2, profile.StyleDescriptors!.Count);
    }

    [Fact]
    public void CarriesPaletteWhenClientHasMarkedColours()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: DateTime.UtcNow,
            Palette: [new AestheticPaletteEntry("#1A2B3C", "Primary", 1)]);

        Assert.Single(profile.Palette!);
        Assert.Equal("#1A2B3C", profile.Palette![0].Hex);
        Assert.Equal("Primary", profile.Palette[0].Role);
    }
}
