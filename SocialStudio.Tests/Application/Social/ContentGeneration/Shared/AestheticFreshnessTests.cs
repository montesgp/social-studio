using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Pure decision-function tests for <see cref="AestheticFreshness.Evaluate"/> and
/// <see cref="AestheticFreshness.ParseSchemaVersion"/> (aesthetic-freshness-versioning).
/// Refs: design — schema-version-aware + completeness-aware freshness.
/// </summary>
public sealed class AestheticFreshnessTests
{
    // ── ParseSchemaVersion ──────────────────────────────────────────────────

    [Theory]
    [InlineData("aesthetic-extractor/v1", 1)]
    [InlineData("aesthetic-extractor/v2", 2)]
    [InlineData("aesthetic-extractor/v3", 3)]
    [InlineData("aesthetic-extractor/v10", 10)]
    public void ParseSchemaVersion_ValidToken_ReturnsTrailingInteger(string version, int expected)
    {
        Assert.Equal(expected, AestheticFreshness.ParseSchemaVersion(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("aesthetic-extractor/vX")]
    [InlineData("aesthetic-extractor/")]
    public void ParseSchemaVersion_UnknownOrMalformed_ReturnsZero(string? version)
    {
        Assert.Equal(0, AestheticFreshness.ParseSchemaVersion(version));
    }

    // ── Evaluate ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_StoredVersionOlderThanCurrent_ReturnsRefresh()
    {
        var snapshot = new AestheticFreshnessSnapshot("aesthetic-extractor/v2", true, true, true);

        var verdict = AestheticFreshness.Evaluate(snapshot);

        Assert.Equal(AestheticFreshnessVerdict.Refresh, verdict);
    }

    [Fact]
    public void Evaluate_SameVersionAndComplete_ReturnsFresh()
    {
        var snapshot = new AestheticFreshnessSnapshot(
            AestheticFreshness.CurrentSchemaVersion, HasColorPairings: true, HasBackgroundPattern: true, HasReferenceImages: true);

        var verdict = AestheticFreshness.Evaluate(snapshot);

        Assert.Equal(AestheticFreshnessVerdict.Fresh, verdict);
    }

    [Fact]
    public void Evaluate_SameVersionAndIncomplete_ReturnsIncomplete_NotRefresh()
    {
        var snapshot = new AestheticFreshnessSnapshot(
            AestheticFreshness.CurrentSchemaVersion, HasColorPairings: false, HasBackgroundPattern: true, HasReferenceImages: true);

        var verdict = AestheticFreshness.Evaluate(snapshot);

        Assert.Equal(AestheticFreshnessVerdict.Incomplete, verdict);
        Assert.NotEqual(AestheticFreshnessVerdict.Refresh, verdict);
    }

    [Fact]
    public void Evaluate_MissingColorPairingsOnly_ReturnsIncomplete()
    {
        var snapshot = new AestheticFreshnessSnapshot(
            AestheticFreshness.CurrentSchemaVersion, HasColorPairings: false, HasBackgroundPattern: true, HasReferenceImages: true);

        Assert.Equal(AestheticFreshnessVerdict.Incomplete, AestheticFreshness.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_MissingBackgroundPatternOnly_ReturnsIncomplete()
    {
        var snapshot = new AestheticFreshnessSnapshot(
            AestheticFreshness.CurrentSchemaVersion, HasColorPairings: true, HasBackgroundPattern: false, HasReferenceImages: true);

        Assert.Equal(AestheticFreshnessVerdict.Incomplete, AestheticFreshness.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_MissingReferenceImagesOnly_ReturnsIncomplete()
    {
        var snapshot = new AestheticFreshnessSnapshot(
            AestheticFreshness.CurrentSchemaVersion, HasColorPairings: true, HasBackgroundPattern: true, HasReferenceImages: false);

        Assert.Equal(AestheticFreshnessVerdict.Incomplete, AestheticFreshness.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_AllPillarsMissing_ReturnsIncomplete()
    {
        var snapshot = new AestheticFreshnessSnapshot(
            AestheticFreshness.CurrentSchemaVersion, HasColorPairings: false, HasBackgroundPattern: false, HasReferenceImages: false);

        Assert.Equal(AestheticFreshnessVerdict.Incomplete, AestheticFreshness.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_UnknownOrNullVersion_ReturnsRefresh()
    {
        // Null version parses to 0 (oldest) — even with all pillars complete, the unknown version
        // still triggers a refresh (safety default: unknown/malformed → treated as oldest).
        var snapshot = new AestheticFreshnessSnapshot(null, true, true, true);

        Assert.Equal(AestheticFreshnessVerdict.Refresh, AestheticFreshness.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_MalformedVersion_ReturnsRefresh()
    {
        var snapshot = new AestheticFreshnessSnapshot("not-a-real-version", true, true, true);

        Assert.Equal(AestheticFreshnessVerdict.Refresh, AestheticFreshness.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_NewerStoredVersionThanCurrent_ReturnsFreshOrIncomplete_NeverRefresh()
    {
        // A stored version NEWER than current (e.g. forward-compatible data) must never be treated
        // as needing an upgrade — only strictly-older versions trigger Refresh.
        var snapshot = new AestheticFreshnessSnapshot("aesthetic-extractor/v99", true, true, true);

        Assert.Equal(AestheticFreshnessVerdict.Fresh, AestheticFreshness.Evaluate(snapshot));
    }
}
