using Incoders.Hive.Domain.UserPreferences;

namespace Incoders.Hive.Tests.Domain;

public class UserPreferenceTests
{
    private static readonly DateTime Now = new(2026, 5, 16, 6, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ThemeKeys.Dark)]
    [InlineData(ThemeKeys.Light)]
    [InlineData(ThemeKeys.System)]
    public void Create_ValidThemeKey_Succeeds(string themeKey)
    {
        var result = UserPreference.Create(
            UserPreferenceId.New(),
            Guid.NewGuid(),
            themeKey,
            null,
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(themeKey, result.Value.ThemeKey);
    }

    [Fact]
    public void Create_InvalidThemeKey_Fails()
    {
        var result = UserPreference.Create(
            UserPreferenceId.New(),
            Guid.NewGuid(),
            "neon-mode",
            null,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("user_preferences.invalid_theme_key", result.Error.Code);
    }

    [Fact]
    public void UpdateTheme_UpdatesThemeAndTimestamp()
    {
        var preference = UserPreference.Create(
            UserPreferenceId.New(),
            Guid.NewGuid(),
            ThemeKeys.Dark,
            null,
            Now).Value;
        var later = Now.AddMinutes(30);

        var result = preference.UpdateTheme(ThemeKeys.Light, "vibrant", later);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeKeys.Light, preference.ThemeKey);
        Assert.Equal("vibrant", preference.AccentTheme);
        Assert.Equal(later, preference.UpdatedAtUtc);
        Assert.Equal(Now, preference.CreatedAtUtc);
    }
}
