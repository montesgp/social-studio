using System.Text.Json;
using Incoders.Hive.Domain.Social.ClientBrandManuals;

namespace Incoders.Hive.Tests.Domain.Social.ClientBrandManuals;

/// <summary>
/// B1a TDD tests for BackgroundEntry tier extension.
/// Spec: BG-01 — BackgroundEntry Extended with Tier Classification.
/// Spec: BG-02 (entry-level guard) — IsFormatDefault=true requires FormatKey + ForcedDefault tier.
/// </summary>
public sealed class BackgroundEntryTests
{
    // -----------------------------------------------------------------------
    // Task 1.1 [TEST — RED] BG-01: legacy JSON deserializes as Creative
    // -----------------------------------------------------------------------

    [Fact]
    public void BackgroundEntry_LegacyJson_NoTierField_DeserializesAsCreative()
    {
        // GIVEN stored JSON background entry with no Tier property
        const string json = """{"Name":"bg","Url":"http://cdn/bg.png"}""";

        // WHEN deserialized into BackgroundEntry
        var entry = JsonSerializer.Deserialize<BackgroundEntry>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // THEN Tier equals BackgroundTier.Creative and no exception is thrown
        Assert.NotNull(entry);
        Assert.Equal(BackgroundTier.Creative, entry!.Tier);
    }

    // -----------------------------------------------------------------------
    // Task 1.2 [TEST — RED] BG-01: full round-trip with explicit tier fields
    // -----------------------------------------------------------------------

    [Fact]
    public void BackgroundEntry_FullRoundTrip_ForcedDefaultFields()
    {
        // GIVEN a BackgroundEntry with Tier=ForcedDefault, FormatKey="flyer", IsFormatDefault=true
        var original = BackgroundEntry.Create(
            "bg", "https://cdn.example.com/bg.png", null,
            BackgroundTier.ForcedDefault, "flyer", true).Value;

        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(original, opts);
        var restored = JsonSerializer.Deserialize<BackgroundEntry>(json, opts);

        // THEN all three new fields round-trip correctly
        Assert.NotNull(restored);
        Assert.Equal(BackgroundTier.ForcedDefault, restored!.Tier);
        Assert.Equal("flyer", restored.FormatKey);
        Assert.True(restored.IsFormatDefault);
    }

    // -----------------------------------------------------------------------
    // Task 1.3 [TEST — RED] BG-02: Create rejects forced default without FormatKey
    // -----------------------------------------------------------------------

    [Fact]
    public void BackgroundEntry_Create_ForcedDefault_NullFormatKey_ReturnsFailure()
    {
        // GIVEN an entry with IsFormatDefault=true and FormatKey=null
        // WHEN validated by Create
        var result = BackgroundEntry.Create(
            "bg", "https://cdn.example.com/bg.png", null,
            BackgroundTier.ForcedDefault, null, true);

        // THEN operation returns a failed Result
        Assert.True(result.IsFailure);
    }
}
