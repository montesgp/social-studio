using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// TDD tests for GenerationPromptContext.AestheticGuidance (design §9 row 8).
/// Refs: CAG-CG-08, CAG-CG-09; tasks 4.6.
///
/// Invariants:
/// - null profile → empty string (no aesthetic text emitted).
/// - VoiceTone non-null → voice/tone snippet present in output.
/// - AverageSlidesPerCarousel present → avg-slides hint present with clamped value (3–10).
/// - avg 12.5 → clamped to 10 in the hint.
/// - avg 1.5 → clamped to 3 in the hint.
/// - null avg → no avg-slides section in output.
/// - VoiceTone null → only avg-slides section (if avg present).
/// - ConcisionConstraint text is STILL present when assembled into a full carousel prompt
///   (AestheticGuidance is ADDITIVE — it appends AFTER ConcisionConstraint).
/// </summary>
public sealed class AestheticGuidanceTests
{
    // ── null profile → empty string ───────────────────────────────────────────

    [Fact]
    public void AestheticGuidance_NullProfile_ReturnsEmptyString()
    {
        var result = GenerationPromptContext.AestheticGuidance(null);

        Assert.Equal(string.Empty, result);
    }

    // ── VoiceTone present → voice/tone text present ───────────────────────────

    [Fact]
    public void AestheticGuidance_WithVoiceTone_ContainsVoiceToneText()
    {
        var profile = new ClientAestheticProfile([], "professional and concise", null, null);

        var result = GenerationPromptContext.AestheticGuidance(profile);

        Assert.Contains("professional and concise", result);
    }

    // ── avg clamping: 12.5 → 10 in the rendered hint ─────────────────────────

    [Fact]
    public void AestheticGuidance_AvgAboveTen_ClampedToTenInHint()
    {
        var profile = new ClientAestheticProfile([], null, 12.5, null);

        var result = GenerationPromptContext.AestheticGuidance(profile);

        // The hint must reference "10" (clamped) and NOT "12" or "13"
        Assert.Contains("10", result);
        Assert.DoesNotContain("12", result);
    }

    // ── avg clamping: 1.5 → 3 in the rendered hint ───────────────────────────

    [Fact]
    public void AestheticGuidance_AvgBelowThree_ClampedToThreeInHint()
    {
        var profile = new ClientAestheticProfile([], null, 1.5, null);

        var result = GenerationPromptContext.AestheticGuidance(profile);

        // The hint must reference "3" (clamped) and NOT "1" or "2"
        Assert.Contains("3", result);
        Assert.DoesNotContain("1.5", result);
        Assert.DoesNotContain("1", result.Replace("3", ""));   // "1" not present when "3" is removed
    }

    // ── null avg → no avg-slides section ─────────────────────────────────────

    [Fact]
    public void AestheticGuidance_NullAvg_NoAvgSlidesSection()
    {
        var profile = new ClientAestheticProfile([], "bold", null, null);

        var result = GenerationPromptContext.AestheticGuidance(profile);

        Assert.DoesNotContain("slides per carousel", result);
        Assert.DoesNotContain("slide", result);
    }

    // ── VoiceTone null → only avg section (if avg present) ───────────────────

    [Fact]
    public void AestheticGuidance_NullVoiceTone_WithAvg_ContainsAvgButNoVoiceText()
    {
        var profile = new ClientAestheticProfile([], null, 5.0, null);

        var result = GenerationPromptContext.AestheticGuidance(profile);

        Assert.DoesNotContain("voice", result.ToLowerInvariant().Replace("advisory", ""));
        Assert.Contains("5", result);  // avg 5.0 → "5" in hint (within 3–10 range, no clamping)
    }

    // ── Both VoiceTone and avg present → both sections present ───────────────

    [Fact]
    public void AestheticGuidance_BothVoiceToneAndAvg_BothPresent()
    {
        var profile = new ClientAestheticProfile([], "educational", 4.0, null);

        var result = GenerationPromptContext.AestheticGuidance(profile);

        Assert.Contains("educational", result);
        Assert.Contains("4", result);
    }

    // ── ADDITIVE contract: ConcisionConstraint text still present in assembled prompt ──

    [Fact]
    public void AestheticGuidance_AssembledAfterConcisionConstraint_ConcisionTextStillPresent()
    {
        // Simulate what CarouselGenerator.BuildPromptJson does:
        // instruction = ... + ConcisionConstraint + " " + AestheticGuidance(profile)
        var profile = new ClientAestheticProfile([], "professional", 5.0, null);
        var assembled = GenerationPromptContext.ConcisionConstraint
            + " "
            + GenerationPromptContext.AestheticGuidance(profile);

        // ConcisionConstraint text must still be fully present
        Assert.Contains(GenerationPromptContext.ConcisionConstraint, assembled);

        // AestheticGuidance must also be present
        Assert.Contains("professional", assembled);
    }

    // ── AestheticGuidance is strictly additive — ConcisionConstraint constant unchanged ──

    [Fact]
    public void ConcisionConstraint_NotModifiedByAestheticGuidance()
    {
        // ConcisionConstraint is a const — its value must not contain any aesthetic-guidance content.
        // Calling AestheticGuidance must not mutate ConcisionConstraint.
        var beforeGuidance = GenerationPromptContext.ConcisionConstraint;
        var profile = new ClientAestheticProfile(["dark studio"], "educational", 5.0, DateTime.UtcNow);
        _ = GenerationPromptContext.AestheticGuidance(profile);
        var afterGuidance = GenerationPromptContext.ConcisionConstraint;

        Assert.Equal(beforeGuidance, afterGuidance);
        Assert.DoesNotContain("educational", afterGuidance);
        Assert.DoesNotContain("CLIENT AESTHETIC", afterGuidance);
    }

    // ── avg exactly at boundary: 3 and 10 → no clamping in hint ─────────────

    [Fact]
    public void AestheticGuidance_AvgExactlyThree_ShowsThreeWithoutClamping()
    {
        var profile = new ClientAestheticProfile([], null, 3.0, null);
        var result = GenerationPromptContext.AestheticGuidance(profile);
        Assert.Contains("3", result);
    }

    [Fact]
    public void AestheticGuidance_AvgExactlyTen_ShowsTenWithoutClamping()
    {
        var profile = new ClientAestheticProfile([], null, 10.0, null);
        var result = GenerationPromptContext.AestheticGuidance(profile);
        Assert.Contains("10", result);
    }
}
