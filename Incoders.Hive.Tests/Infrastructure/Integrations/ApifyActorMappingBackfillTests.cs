using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Tests.Infrastructure.Integrations;

/// <summary>
/// Unit tests for the pure transformation function in <see cref="ApifyActorMappingBackfill"/>.
/// The function is tested independently of encryption, DI, and database — it is a pure
/// List&lt;entry&gt; → List&lt;entry&gt; mapping that can be validated with plain data.
///
/// Transformation rule:
///   For entries whose variantKey is null, empty, or "default":
///     - The LAST TWO entries in the array → variantKey "profile"
///     - All earlier entries → variantKey "social-extraction"
///   Within each resulting (networkKey, variantKey) group, set isPrimary=true on the FIRST entry
///   (array order after re-tagging) and false on the rest.
///   Entries already having an explicit non-default, non-empty variantKey are left unchanged
///   (idempotent — running twice is a no-op).
/// </summary>
public sealed class ApifyActorMappingBackfillTests
{
    // ─── Single entry: only 1 → it is "profile" (last 2 = all entries when N≤2) ──

    [Fact]
    public void Transform_SingleEntry_BecomesProfile_IsPrimary()
    {
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", "default", "actor-1", false)
        };

        var result = ApifyActorMappingBackfill.Transform(input);

        Assert.Single(result);
        Assert.Equal("instagram", result[0].NetworkKey);
        Assert.Equal("profile", result[0].VariantKey);
        Assert.Equal("actor-1", result[0].ActorId);
        Assert.True(result[0].IsPrimary);
    }

    // ─── Two entries: both become "profile" ──────────────────────────────────

    [Fact]
    public void Transform_TwoEntries_BothBecomeProfile_FirstIsPrimary()
    {
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", "default", "actor-1", false),
            new("instagram", "default", "actor-2", false)
        };

        var result = ApifyActorMappingBackfill.Transform(input);

        Assert.Equal(2, result.Count);
        Assert.Equal("profile", result[0].VariantKey);
        Assert.Equal("profile", result[1].VariantKey);
        Assert.True(result[0].IsPrimary);  // first of "profile" group
        Assert.False(result[1].IsPrimary);
    }

    // ─── Three entries: first → social-extraction; last 2 → profile ───────────

    [Fact]
    public void Transform_ThreeEntries_FirstSocialExtraction_LastTwoProfile()
    {
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", "default", "actor-1", false),
            new("instagram", "default", "actor-2", false),
            new("instagram", "default", "actor-3", false)
        };

        var result = ApifyActorMappingBackfill.Transform(input);

        Assert.Equal(3, result.Count);

        // actor-1: social-extraction, primary of its group
        Assert.Equal("social-extraction", result[0].VariantKey);
        Assert.True(result[0].IsPrimary);

        // actor-2: profile, primary of its group
        Assert.Equal("profile", result[1].VariantKey);
        Assert.True(result[1].IsPrimary);

        // actor-3: profile, backup
        Assert.Equal("profile", result[2].VariantKey);
        Assert.False(result[2].IsPrimary);
    }

    // ─── Four entries: first two → social-extraction; last two → profile ──────

    [Fact]
    public void Transform_FourEntries_FirstTwoSocialExtraction_LastTwoProfile()
    {
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", "default", "actor-1", false),
            new("youtube",   "default", "actor-2", false),
            new("instagram", "default", "actor-3", false),
            new("instagram", "default", "actor-4", false)
        };

        var result = ApifyActorMappingBackfill.Transform(input);

        Assert.Equal(4, result.Count);
        Assert.Equal("social-extraction", result[0].VariantKey); // actor-1
        Assert.Equal("social-extraction", result[1].VariantKey); // actor-2
        Assert.Equal("profile", result[2].VariantKey);           // actor-3
        Assert.Equal("profile", result[3].VariantKey);           // actor-4

        // isPrimary: first of each (networkKey, variantKey) group
        Assert.True(result[0].IsPrimary);   // first social-extraction instagram
        Assert.True(result[1].IsPrimary);   // first social-extraction youtube
        Assert.True(result[2].IsPrimary);   // first profile instagram
        Assert.False(result[3].IsPrimary);  // second profile instagram
    }

    // ─── Null / missing variantKey treated same as "default" ─────────────────

    [Fact]
    public void Transform_NullVariantKey_TreatedAsDefault()
    {
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", null, "actor-1", false),
            new("instagram", null, "actor-2", false)
        };

        var result = ApifyActorMappingBackfill.Transform(input);

        Assert.Equal("profile", result[0].VariantKey);
        Assert.Equal("profile", result[1].VariantKey);
        Assert.True(result[0].IsPrimary);
        Assert.False(result[1].IsPrimary);
    }

    // ─── Empty variantKey treated same as "default" ───────────────────────────

    [Fact]
    public void Transform_EmptyVariantKey_TreatedAsDefault()
    {
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", "", "actor-1", false),
            new("instagram", "", "actor-2", false)
        };

        var result = ApifyActorMappingBackfill.Transform(input);

        Assert.Equal("profile", result[0].VariantKey);
        Assert.Equal("profile", result[1].VariantKey);
    }

    // ─── Idempotent: already-tagged entries are left unchanged ───────────────

    [Fact]
    public void Transform_AlreadyTaggedEntries_Idempotent()
    {
        // Simulate running the backfill twice: second run should be a no-op
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", "social-extraction", "actor-1", true),
            new("instagram", "profile",           "actor-2", true),
            new("instagram", "profile",           "actor-3", false)
        };

        var result = ApifyActorMappingBackfill.Transform(input);

        Assert.Equal(3, result.Count);
        // social-extraction entry unchanged
        Assert.Equal("social-extraction", result[0].VariantKey);
        Assert.True(result[0].IsPrimary);
        // profile entries unchanged
        Assert.Equal("profile", result[1].VariantKey);
        Assert.True(result[1].IsPrimary);
        Assert.Equal("profile", result[2].VariantKey);
        Assert.False(result[2].IsPrimary);
    }

    // ─── Mixed: some already tagged, some still "default" ────────────────────

    [Fact]
    public void Transform_MixedTaggedAndDefault_OnlyDefaultsAreTransformed()
    {
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", "social-extraction", "actor-already", true),
            new("instagram", "default",           "actor-default-1", false),
            new("instagram", "default",           "actor-default-2", false)
        };

        var result = ApifyActorMappingBackfill.Transform(input);

        Assert.Equal(3, result.Count);
        // Pre-tagged entry untouched
        Assert.Equal("social-extraction", result[0].VariantKey);
        Assert.True(result[0].IsPrimary);
        // "default" entries transformed per rule
        Assert.Equal("profile", result[1].VariantKey);
        Assert.Equal("profile", result[2].VariantKey);
    }

    // ─── Empty input → empty output ───────────────────────────────────────────

    [Fact]
    public void Transform_EmptyInput_ReturnsEmpty()
    {
        var result = ApifyActorMappingBackfill.Transform([]);
        Assert.Empty(result);
    }

    // ─── HasAnyDefaultEntries: returns true only when "default"/null/empty entries exist ──

    [Fact]
    public void HasAnyDefaultEntries_WhenDefaultEntriesExist_ReturnsTrue()
    {
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", "default", "actor-1", false)
        };
        Assert.True(ApifyActorMappingBackfill.HasAnyDefaultEntries(input));
    }

    [Fact]
    public void HasAnyDefaultEntries_WhenAllAlreadyTagged_ReturnsFalse()
    {
        var input = new List<ApifyActorMappingBackfill.RawEntry>
        {
            new("instagram", "social-extraction", "actor-1", true),
            new("instagram", "profile",           "actor-2", true)
        };
        Assert.False(ApifyActorMappingBackfill.HasAnyDefaultEntries(input));
    }
}
