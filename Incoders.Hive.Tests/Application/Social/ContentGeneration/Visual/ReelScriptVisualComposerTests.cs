using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ReelScript;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// TDD tests for <see cref="ReelScriptVisualComposer"/> body structure.
/// RS-02 scenarios: newline-separated scenes, bold timestamps, emoji preserved, CTA present.
/// </summary>
public sealed class ReelScriptVisualComposerTests
{
    private static readonly BrandSnapshot NoBrand = BrandSnapshot.FromBrandData(null, null, null);

    // ------------------------------------------------------------------
    // RS-02 — Multi-scene body contains newlines (not space-joined)
    // ------------------------------------------------------------------

    [Fact]
    public async Task MultiScene_Body_ContainsNewlines()
    {
        // RS-02 scenario: "Multi-scene body contains newlines"
        var payload = new ReelScriptContent(
            Hook: "Opening hook text",
            Scenes:
            [
                new ReelScriptScene("0:00", "First voiceover", "First on-screen text"),
                new ReelScriptScene("0:05", "Second voiceover", "Second on-screen text"),
            ],
            CtaText: "Book a call",
            SuggestedMusicMood: null);

        var service = new VisualCompositionService();
        var spec = await service.ComposeAsync("reel-script", payload, NoBrand, CancellationToken.None);

        Assert.NotNull(spec.TextBody);
        Assert.Contains("\n", spec.TextBody);
        // Must NOT be a single space-joined line (no scene separator as single space)
        Assert.DoesNotContain("0:00: First voiceover (First on-screen text) 0:05:", spec.TextBody);
    }

    // ------------------------------------------------------------------
    // RS-02 — Each scene begins with bold timestamp
    // ------------------------------------------------------------------

    [Fact]
    public async Task Scene_Body_StartsWithBoldTimestamp()
    {
        // RS-02 scenario: "Each scene begins with bold timestamp"
        var payload = new ReelScriptContent(
            Hook: "Hook",
            Scenes:
            [
                new ReelScriptScene("0:00", "Voiceover text", "On screen"),
            ],
            CtaText: "CTA text",
            SuggestedMusicMood: null);

        var service = new VisualCompositionService();
        var spec = await service.ComposeAsync("reel-script", payload, NoBrand, CancellationToken.None);

        Assert.NotNull(spec.TextBody);
        Assert.Contains("**0:00**", spec.TextBody);
    }

    // ------------------------------------------------------------------
    // RS-02 — Emojis are preserved verbatim
    // ------------------------------------------------------------------

    [Fact]
    public async Task EmojiInVoiceover_Preserved()
    {
        // RS-02 scenario: "Emojis are preserved verbatim"
        var payload = new ReelScriptContent(
            Hook: "Hook",
            Scenes:
            [
                new ReelScriptScene("0:00", "🎬 Cinematic voiceover", "Scene text"),
            ],
            CtaText: "CTA",
            SuggestedMusicMood: null);

        var service = new VisualCompositionService();
        var spec = await service.ComposeAsync("reel-script", payload, NoBrand, CancellationToken.None);

        Assert.NotNull(spec.TextBody);
        Assert.Contains("🎬", spec.TextBody);
    }

    // ------------------------------------------------------------------
    // RS-02 — CTA appears after the last scene
    // ------------------------------------------------------------------

    [Fact]
    public async Task CTA_AppearsAfterLastScene()
    {
        // RS-02 scenario: "CTA present after last scene"
        const string ctaText = "Book your free consultation";
        var payload = new ReelScriptContent(
            Hook: "Hook",
            Scenes:
            [
                new ReelScriptScene("0:00", "First voiceover", "First on-screen"),
                new ReelScriptScene("0:05", "Last voiceover", "Last on-screen"),
            ],
            CtaText: ctaText,
            SuggestedMusicMood: null);

        var service = new VisualCompositionService();
        var spec = await service.ComposeAsync("reel-script", payload, NoBrand, CancellationToken.None);

        Assert.NotNull(spec.TextBody);
        var ctaIndex = spec.TextBody.IndexOf(ctaText, StringComparison.Ordinal);
        var lastSceneIndex = spec.TextBody.IndexOf("0:05", StringComparison.Ordinal);

        Assert.True(ctaIndex > -1, "CTA text must appear in the body");
        Assert.True(lastSceneIndex > -1, "Last scene timestamp must appear in the body");
        Assert.True(ctaIndex > lastSceneIndex, "CTA must appear after the last scene");
    }
}
