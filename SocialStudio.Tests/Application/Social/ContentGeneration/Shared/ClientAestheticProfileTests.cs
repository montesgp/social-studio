using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// TDD tests for WU-2 step 2.1: ClientAestheticProfile value object.
/// Refs: design §3.1; tasks 2.1; CAG-CG-05.
/// </summary>
public sealed class ClientAestheticProfileTests
{
    // ── 2.1a — All four fields are present ──────────────────────────────────

    [Fact]
    public void ClientAestheticProfile_HasAllFourRequiredFields()
    {
        var descriptors = new List<string> { "dark background", "minimalist" };
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: descriptors,
            VoiceTone: "professional",
            AverageSlidesPerCarousel: 5.2,
            AnalyzedAtUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(descriptors, profile.BackgroundDescriptors);
        Assert.Equal("professional", profile.VoiceTone);
        Assert.Equal(5.2, profile.AverageSlidesPerCarousel);
        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), profile.AnalyzedAtUtc);
    }

    // ── 2.1b — Null-analysis shape (empty descriptors, all nulls) ───────────

    [Fact]
    public void ClientAestheticProfile_NullAnalysisShape_AllNullableFieldsNull()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        Assert.Empty(profile.BackgroundDescriptors);
        Assert.Null(profile.VoiceTone);
        Assert.Null(profile.AverageSlidesPerCarousel);
        Assert.Null(profile.AnalyzedAtUtc);
    }

    // ── 2.1c — Phase 2 forward-compat: BackgroundImageUrls nullable field ───
    // The record must be constructable without BackgroundImageUrls so existing
    // callers never break when the optional field is added in Phase 2.

    [Fact]
    public void ClientAestheticProfile_ExistingConstructionUnaffectedByFutureNullableField()
    {
        // Construction with only the Phase 1 fields succeeds.
        // If Phase 2 adds IReadOnlyList<string>? BackgroundImageUrls with a null default,
        // this test must still pass without change.
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: ["clean aesthetic"],
            VoiceTone: "casual",
            AverageSlidesPerCarousel: 3.0,
            AnalyzedAtUtc: DateTime.UtcNow);

        Assert.NotNull(profile);
        Assert.Single(profile.BackgroundDescriptors);
    }

    // ── 2.1d — BackgroundDescriptors is IReadOnlyList<string> ───────────────

    [Fact]
    public void ClientAestheticProfile_BackgroundDescriptors_IsReadOnlyList()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: ["a", "b", "c"],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        Assert.IsAssignableFrom<IReadOnlyList<string>>(profile.BackgroundDescriptors);
        Assert.Equal(3, profile.BackgroundDescriptors.Count);
    }

    // ── 2.1e — Same list instance and same scalar fields → equal records ────

    [Fact]
    public void ClientAestheticProfile_RecordEquality_SameListInstance_EqualRecords()
    {
        // IReadOnlyList<string> uses reference equality in record's synthesized Equals.
        // Same list instance + same scalars → equal.
        var descriptors = new List<string> { "desc" };
        var a = new ClientAestheticProfile(descriptors, "professional", 5.0, null);
        var b = new ClientAestheticProfile(descriptors, "professional", 5.0, null);

        Assert.Equal(a, b);
    }
}
