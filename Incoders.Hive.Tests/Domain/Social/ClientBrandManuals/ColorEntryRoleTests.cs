using Incoders.Hive.Domain.Social.ClientBrandManuals;

namespace Incoders.Hive.Tests.Domain.Social.ClientBrandManuals;

/// <summary>
/// TDD tests for WU-1 Phase 1.A: ColorEntry structured ColorRole + int Rank.
/// Refs: CG-COL-01; design §3.2 (§6 item 1).
/// </summary>
public sealed class ColorEntryRoleTests
{
    // -----------------------------------------------------------------------
    // 1.1 RED: ColorEntry.Create accepts ColorRole? + int Rank
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_WithPrimaryRole_Rank1_Succeeds()
    {
        var result = ColorEntry.Create("Brand Red", "#FF0000", ColorRole.Primary, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(ColorRole.Primary, result.Value.Role);
        Assert.Equal(1, result.Value.Rank);
    }

    [Fact]
    public void Create_WithNullRole_Rank0_Succeeds()
    {
        var result = ColorEntry.Create("Neutral", "#CCCCCC", null, 0);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Role);
        Assert.Equal(0, result.Value.Rank);
    }

    [Theory]
    [InlineData(ColorRole.Primary)]
    [InlineData(ColorRole.Secondary)]
    [InlineData(ColorRole.Accent)]
    [InlineData(ColorRole.Neutral)]
    public void Create_AllRoleValues_Succeed(ColorRole role)
    {
        var result = ColorEntry.Create("Color", "#AABBCC", role, 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(role, result.Value.Role);
    }

    [Fact]
    public void Create_NegativeRank_ReturnsFailure()
    {
        var result = ColorEntry.Create("Color", "#AABBCC", null, -1);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_ZeroRank_Succeeds()
    {
        var result = ColorEntry.Create("Color", "#AABBCC", ColorRole.Accent, 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Rank);
    }

    // -----------------------------------------------------------------------
    // Free-text migration map: TryParseRole
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("primary", ColorRole.Primary)]
    [InlineData("PRIMARY", ColorRole.Primary)]
    [InlineData("Primary", ColorRole.Primary)]
    [InlineData("secondary", ColorRole.Secondary)]
    [InlineData("Secondary", ColorRole.Secondary)]
    [InlineData("accent", ColorRole.Accent)]
    [InlineData("Accent", ColorRole.Accent)]
    [InlineData("neutral", ColorRole.Neutral)]
    [InlineData("Neutral", ColorRole.Neutral)]
    public void TryParseRole_KnownFreeText_MapsCorrectly(string freeText, ColorRole expected)
    {
        var result = ColorEntry.TryParseRole(freeText);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("tertiary")]
    [InlineData("brand-color")]
    public void TryParseRole_UnknownOrNull_ReturnsNull(string? freeText)
    {
        var result = ColorEntry.TryParseRole(freeText);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Restore: back-compat tolerates null role + zero rank (legacy rows)
    // -----------------------------------------------------------------------

    [Fact]
    public void Restore_NullRole_ZeroRank_DoesNotThrow()
    {
        var entry = ColorEntry.Restore("Legacy", "#FFFFFF", null, 0);

        Assert.NotNull(entry);
        Assert.Null(entry.Role);
        Assert.Equal(0, entry.Rank);
    }

    [Fact]
    public void Restore_WithRole_AndRank_PreservesValues()
    {
        var entry = ColorEntry.Restore("Blue", "#0000FF", ColorRole.Secondary, 2);

        Assert.Equal(ColorRole.Secondary, entry.Role);
        Assert.Equal(2, entry.Rank);
    }
}
