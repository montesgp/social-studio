using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-2 RED tests: ClientAestheticProfile.CreativeSlideIndices additive nullable field.
/// Asserts:
/// - Construction with CreativeSlideIndices = null compiles; existing construction sites unchanged.
/// - `with` expression changing only CreativeSlideIndices compiles.
/// - Field is IReadOnlyList&lt;int&gt;?.
/// Refs: CIG-BSP-02, CIG-BSP-03; design §0 OQ-1, OQ-2, §7; tasks 2.1.
/// </summary>
public sealed class CreativeSlideIndicesTests
{
    // -----------------------------------------------------------------------
    // Back-compat: existing construction sites (Phase 1 only fields) still compile
    // -----------------------------------------------------------------------

    [Fact]
    public void ClientAestheticProfile_ConstructionWithoutCreativeSlideIndices_CompilesAndDefaultsToNull()
    {
        // GIVEN construction using only Phase 1 positional parameters
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: ["dark botanical"],
            VoiceTone: "warm and approachable",
            AverageSlidesPerCarousel: 5.0,
            AnalyzedAtUtc: DateTime.UtcNow);

        // THEN CreativeSlideIndices defaults to null (additive trailing nullable)
        Assert.Null(profile.CreativeSlideIndices);
    }

    [Fact]
    public void ClientAestheticProfile_ConstructionWithExplicitNull_CompilesAndIsNull()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            CreativeSlideIndices: null);

        Assert.Null(profile.CreativeSlideIndices);
    }

    [Fact]
    public void ClientAestheticProfile_ConstructionWithExplicitList_StoresCorrectly()
    {
        IReadOnlyList<int> indices = [1, 4];

        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            CreativeSlideIndices: indices);

        Assert.NotNull(profile.CreativeSlideIndices);
        Assert.Equal(indices, profile.CreativeSlideIndices);
    }

    // -----------------------------------------------------------------------
    // with-expression: changing only CreativeSlideIndices compiles
    // -----------------------------------------------------------------------

    [Fact]
    public void ClientAestheticProfile_WithExpression_OnlyCreativeSlideIndices_Compiles()
    {
        var original = new ClientAestheticProfile(
            BackgroundDescriptors: ["minimalist"],
            VoiceTone: "bold",
            AverageSlidesPerCarousel: 4.0,
            AnalyzedAtUtc: null);

        // WHEN a with-expression changes only CreativeSlideIndices
        var updated = original with { CreativeSlideIndices = [1, 5] };

        // THEN the new instance has the updated value; original fields are preserved
        Assert.Equal([1, 5], updated.CreativeSlideIndices);
        Assert.Equal(original.BackgroundDescriptors, updated.BackgroundDescriptors);
        Assert.Equal(original.VoiceTone, updated.VoiceTone);
        Assert.Equal(original.AverageSlidesPerCarousel, updated.AverageSlidesPerCarousel);
        Assert.Equal(original.AnalyzedAtUtc, updated.AnalyzedAtUtc);
    }

    [Fact]
    public void ClientAestheticProfile_WithExpression_ClearsCreativeSlideIndices_ToNull()
    {
        var original = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            CreativeSlideIndices: [1, 3]);

        var cleared = original with { CreativeSlideIndices = null };

        Assert.Null(cleared.CreativeSlideIndices);
    }

    // -----------------------------------------------------------------------
    // Field type is IReadOnlyList<int>?
    // -----------------------------------------------------------------------

    [Fact]
    public void ClientAestheticProfile_CreativeSlideIndices_IsReadOnlyListOfInt()
    {
        var prop = typeof(ClientAestheticProfile)
            .GetProperty(nameof(ClientAestheticProfile.CreativeSlideIndices));

        Assert.NotNull(prop);
        Assert.Equal(typeof(IReadOnlyList<int>), prop!.PropertyType);
    }
}
