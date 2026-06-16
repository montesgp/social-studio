using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Domain.Social.ClientBrandManuals;

/// <summary>
/// B1a TDD tests for ClientBrandManual forced-default uniqueness invariant.
/// Spec: BG-02 — At Most One IsFormatDefault per (FormatKey).
/// Tasks: 1.6 (first forced default accepted), 1.7 (second rejected), 1.8 (different formats accepted).
/// </summary>
public sealed class ClientBrandManualForcedDefaultTests
{
    private static readonly DateTime Now = new(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ClientId ValidClientId = ClientId.New();
    private static readonly Guid ValidOrgId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // Task 1.6 [TEST — RED] BG-02: first forced default for format accepted
    // -----------------------------------------------------------------------

    [Fact]
    public void ClientBrandManual_FirstForcedDefault_SameFormat_Succeeds()
    {
        // GIVEN no existing ForcedDefault for FormatKey="story"
        var entry = BackgroundEntry.Create("bg", "https://cdn.example.com/bg.png", null,
            BackgroundTier.ForcedDefault, "story", true).Value;

        // WHEN entry with IsFormatDefault=true, FormatKey="story" is added
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "My Manual",
            null, null,
            new[] { entry },
            null,
            null, null, Now);

        // THEN operation succeeds
        Assert.True(result.IsSuccess);
    }

    // -----------------------------------------------------------------------
    // Task 1.7 [TEST — RED] BG-02: second forced default for same format rejected
    // -----------------------------------------------------------------------

    [Fact]
    public void ClientBrandManual_SecondForcedDefault_SameFormat_Fails()
    {
        // GIVEN one entry with IsFormatDefault=true for FormatKey="story" already exists
        var entry1 = BackgroundEntry.Create("bg1", "https://cdn.example.com/bg1.png", null,
            BackgroundTier.ForcedDefault, "story", true).Value;
        var entry2 = BackgroundEntry.Create("bg2", "https://cdn.example.com/bg2.png", null,
            BackgroundTier.ForcedDefault, "story", true).Value;

        // WHEN a second entry with IsFormatDefault=true, FormatKey="story" is added
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "My Manual",
            null, null,
            new[] { entry1, entry2 },
            null,
            null, null, Now);

        // THEN operation returns a failed Result
        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.duplicate-forced-default", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Task 1.8 [TEST — RED] BG-02: two forced defaults for different formats accepted
    // -----------------------------------------------------------------------

    [Fact]
    public void ClientBrandManual_ForcedDefaults_DifferentFormats_Succeed()
    {
        // GIVEN ForcedDefault for FormatKey="story" AND ForcedDefault for FormatKey="carousel"
        var flyerEntry = BackgroundEntry.Create("bg-flyer", "https://cdn.example.com/bg-flyer.png", null,
            BackgroundTier.ForcedDefault, "story", true).Value;
        var carouselEntry = BackgroundEntry.Create("bg-carousel", "https://cdn.example.com/bg-carousel.png", null,
            BackgroundTier.ForcedDefault, "carousel", true).Value;

        // WHEN both are used to Create the manual
        var result = ClientBrandManual.Create(
            ClientBrandManualId.New(), ValidClientId, ValidOrgId,
            "My Manual",
            null, null,
            new[] { flyerEntry, carouselEntry },
            null,
            null, null, Now);

        // THEN both entries coexist successfully
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.BackgroundTemplates.Count);
    }
}
