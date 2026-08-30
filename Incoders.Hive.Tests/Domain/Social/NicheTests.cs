using Incoders.Hive.Domain.Social.Niches;

namespace Incoders.Hive.Tests.Domain.Social;

public sealed class NicheTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidValues_NormalizesCodeAndTrimsDisplayName()
    {
        var result = Niche.Create("  Software  ", "  Software & Technology  ", "cpu", ["#Software", "technology"], Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("software", result.Value.Code);
        Assert.Equal("Software & Technology", result.Value.DisplayName);
        Assert.Equal("cpu", result.Value.IconKey);
        Assert.True(result.Value.IsActive);
        Assert.Equal(Now, result.Value.CreatedAtUtc);
    }

    [Fact]
    public void Create_WithoutIconKey_LeavesIconKeyNullForGenericFallback()
    {
        var result = Niche.Create("software", "Software", null, [], Now);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.IconKey);
    }

    [Fact]
    public void Create_NormalizesHashtags_StrippingHashLoweringAndDeduplicating()
    {
        var result = Niche.Create(
            "medical-health",
            "Medical & Health",
            null,
            ["  #Medical ", "HEALTH", "medical", "#health"],
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "medical", "health" }, result.Value.SearchHashtags);
    }

    [Fact]
    public void Create_WithInvalidCode_Fails()
    {
        var result = Niche.Create("Not Valid!", "Whatever", null, [], Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.invalid_code", result.Error.Code);
    }

    [Fact]
    public void Create_WithEmptyDisplayName_Fails()
    {
        var result = Niche.Create("software", "   ", null, [], Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.invalid_display_name", result.Error.Code);
    }

    [Fact]
    public void Create_WithInvalidIconKey_Fails()
    {
        var result = Niche.Create("software", "Software", "Not A Key", [], Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.invalid_icon_key", result.Error.Code);
    }

    [Fact]
    public void Create_WithInvalidHashtag_Fails()
    {
        var result = Niche.Create("software", "Software", null, ["not a hashtag"], Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.invalid_hashtag", result.Error.Code);
    }

    [Fact]
    public void Create_WithTooManyHashtags_Fails()
    {
        var hashtags = Enumerable.Range(0, Niche.MaxSearchHashtags + 1).Select(i => $"tag{i}").ToArray();

        var result = Niche.Create("software", "Software", null, hashtags, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.invalid_hashtag", result.Error.Code);
    }

    [Fact]
    public void Update_ChangesDisplayNameIconAndHashtags_ButNeverCode()
    {
        var niche = Niche.Create("software", "Software", null, ["software"], Now).Value;
        var later = Now.AddDays(1);

        var result = niche.Update("Software & AI", "cpu", ["software", "ai"], later);

        Assert.True(result.IsSuccess);
        Assert.Equal("software", niche.Code);
        Assert.Equal("Software & AI", niche.DisplayName);
        Assert.Equal("cpu", niche.IconKey);
        Assert.Equal(new[] { "software", "ai" }, niche.SearchHashtags);
        Assert.Equal(later, niche.UpdatedAtUtc);
    }

    [Fact]
    public void Deactivate_ThenActivate_TogglesIsActive()
    {
        var niche = Niche.Create("software", "Software", null, [], Now).Value;

        Assert.True(niche.Deactivate(Now).IsSuccess);
        Assert.False(niche.IsActive);
        Assert.True(niche.Deactivate(Now).IsFailure);

        Assert.True(niche.Activate(Now).IsSuccess);
        Assert.True(niche.IsActive);
        Assert.True(niche.Activate(Now).IsFailure);
    }
}
