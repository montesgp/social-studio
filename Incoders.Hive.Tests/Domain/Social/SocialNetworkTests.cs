using Incoders.Hive.Domain.Social.Networks;

namespace Incoders.Hive.Tests.Domain.Social;

public sealed class SocialNetworkTests
{
    private static readonly DateTime Now = new(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);

    // --- Create ---

    [Fact]
    public void Create_WithValidInputs_ReturnsActiveSocialNetwork()
    {
        var result = SocialNetwork.Create("instagram", "Instagram", "instagram", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("instagram", result.Value.Code);
        Assert.Equal("Instagram", result.Value.DisplayName);
        Assert.Equal("instagram", result.Value.IconKey);
        Assert.True(result.Value.IsActive);
        Assert.Equal(Now, result.Value.CreatedAtUtc);
        Assert.Equal(Now, result.Value.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithUppercaseCode_ReturnsValidationError()
    {
        var result = SocialNetwork.Create("Instagram", "Instagram", "instagram", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_code", result.Error.Code);
    }

    [Fact]
    public void Create_WithSpecialCharactersInCode_ReturnsValidationError()
    {
        var result = SocialNetwork.Create("insta_gram", "Instagram", "instagram", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_code", result.Error.Code);
    }

    [Fact]
    public void Create_WithCodeTooShort_ReturnsValidationError()
    {
        var result = SocialNetwork.Create("x", "X", "x", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_code", result.Error.Code);
    }

    [Fact]
    public void Create_WithCodeTooLong_ReturnsValidationError()
    {
        var code = new string('a', 33);
        var result = SocialNetwork.Create(code, "Display", "icon", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_code", result.Error.Code);
    }

    [Fact]
    public void Create_NormalizesCodeToLowercase()
    {
        // Whitespace is trimmed; code is stored trimmed
        var result = SocialNetwork.Create("  instagram  ", "Instagram", "instagram", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("instagram", result.Value.Code);
    }

    [Fact]
    public void Create_WithEmptyDisplayName_ReturnsValidationError()
    {
        var result = SocialNetwork.Create("instagram", "", "instagram", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_display_name", result.Error.Code);
    }

    [Fact]
    public void Create_WithDisplayNameExceedingMaxLength_ReturnsValidationError()
    {
        var displayName = new string('x', 81);
        var result = SocialNetwork.Create("instagram", displayName, "instagram", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_display_name", result.Error.Code);
    }

    [Fact]
    public void Create_WithInvalidIconKey_ReturnsValidationError()
    {
        var result = SocialNetwork.Create("instagram", "Instagram", "", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_icon_key", result.Error.Code);
    }

    // --- IconKey pattern validation (R-D-005) ---

    [Theory]
    [InlineData("My-Icon")]     // hyphen not allowed
    [InlineData("BadIcon")]     // uppercase not allowed
    [InlineData("icon!")]       // special character not allowed
    [InlineData("icon space")]  // space not allowed
    public void Create_WithIconKeyViolatingPattern_ReturnsInvalidIconKeyError(string invalidIconKey)
    {
        var result = SocialNetwork.Create("instagram", "Instagram", invalidIconKey, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_icon_key", result.Error.Code);
    }

    [Fact]
    public void Create_WithLowercaseAlphanumericIconKey_Succeeds()
    {
        var result = SocialNetwork.Create("instagram", "Instagram", "icon42", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("icon42", result.Value.IconKey);
    }

    // --- Rename ---

    [Fact]
    public void Rename_WithValidInputs_UpdatesDisplayNameAndIconKey()
    {
        var network = CreateValid();
        var later = Now.AddHours(1);

        var result = network.Rename("TikTok", "tiktok2", later);

        Assert.True(result.IsSuccess);
        Assert.Equal("TikTok", network.DisplayName);
        Assert.Equal("tiktok2", network.IconKey);
        Assert.Equal(later, network.UpdatedAtUtc);
    }

    [Fact]
    public void Rename_WithEmptyDisplayName_ReturnsValidationError()
    {
        var network = CreateValid();

        var result = network.Rename("", "instagram", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.invalid_display_name", result.Error.Code);
    }

    [Fact]
    public void Rename_DoesNotChangeCode()
    {
        var network = CreateValid();
        var originalCode = network.Code;

        network.Rename("New Name", "newicon", Now);

        Assert.Equal(originalCode, network.Code);
    }

    // --- Deactivate ---

    [Fact]
    public void Deactivate_WhenActive_SetsIsActiveFalse()
    {
        var network = CreateValid();

        var result = network.Deactivate(Now);

        Assert.True(result.IsSuccess);
        Assert.False(network.IsActive);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_ReturnsAlreadyInactiveError()
    {
        var network = CreateValid();
        network.Deactivate(Now);

        var result = network.Deactivate(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.already_inactive", result.Error.Code);
    }

    // --- Activate ---

    [Fact]
    public void Activate_WhenInactive_SetsIsActiveTrue()
    {
        var network = CreateValid();
        network.Deactivate(Now);

        var result = network.Activate(Now);

        Assert.True(result.IsSuccess);
        Assert.True(network.IsActive);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_ReturnsAlreadyActiveError()
    {
        var network = CreateValid();

        var result = network.Activate(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("social.networks.already_active", result.Error.Code);
    }

    private static SocialNetwork CreateValid()
    {
        var result = SocialNetwork.Create("instagram", "Instagram", "instagram", Now);
        return result.Value;
    }
}
