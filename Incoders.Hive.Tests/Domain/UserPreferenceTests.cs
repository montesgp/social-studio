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
            DateFormats.Default,
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
            DateFormats.Default,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("user_preferences.invalid_theme_key", result.Error.Code);
    }

    [Theory]
    [InlineData(DateFormats.DdMmYyyy)]
    [InlineData(DateFormats.MmDdYyyy)]
    [InlineData(DateFormats.YyyyMmDd)]
    public void Create_ValidDateFormat_Succeeds(string dateFormat)
    {
        var result = UserPreference.Create(
            UserPreferenceId.New(),
            Guid.NewGuid(),
            ThemeKeys.Dark,
            null,
            dateFormat,
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(dateFormat, result.Value.DateFormat);
    }

    [Fact]
    public void Create_InvalidDateFormat_Fails()
    {
        var result = UserPreference.Create(
            UserPreferenceId.New(),
            Guid.NewGuid(),
            ThemeKeys.Dark,
            null,
            "DD.MM.YYYY",
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("user_preferences.invalid_date_format", result.Error.Code);
    }

    [Fact]
    public void Create_NoDateFormat_DefaultsToSystemDefault()
    {
        var result = UserPreference.Create(
            UserPreferenceId.New(),
            Guid.NewGuid(),
            ThemeKeys.Dark,
            null,
            DateFormats.Default,
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateFormats.Default, result.Value.DateFormat);
    }

    [Fact]
    public void UpdateTheme_UpdatesThemeAndTimestamp()
    {
        var preference = UserPreference.Create(
            UserPreferenceId.New(),
            Guid.NewGuid(),
            ThemeKeys.Dark,
            null,
            DateFormats.Default,
            Now).Value;
        var later = Now.AddMinutes(30);

        var result = preference.UpdateTheme(ThemeKeys.Light, "vibrant", DateFormats.Default, later);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThemeKeys.Light, preference.ThemeKey);
        Assert.Equal("vibrant", preference.AccentTheme);
        Assert.Equal(later, preference.UpdatedAtUtc);
        Assert.Equal(Now, preference.CreatedAtUtc);
    }

    [Fact]
    public void UpdateTheme_UpdatesDateFormat()
    {
        var preference = UserPreference.Create(
            UserPreferenceId.New(),
            Guid.NewGuid(),
            ThemeKeys.Dark,
            null,
            DateFormats.Default,
            Now).Value;
        var later = Now.AddMinutes(30);

        var result = preference.UpdateTheme(ThemeKeys.Dark, null, DateFormats.YyyyMmDd, later);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateFormats.YyyyMmDd, preference.DateFormat);
    }

    [Fact]
    public void UpdateTheme_InvalidDateFormat_Fails()
    {
        var preference = UserPreference.Create(
            UserPreferenceId.New(),
            Guid.NewGuid(),
            ThemeKeys.Dark,
            null,
            DateFormats.Default,
            Now).Value;

        var result = preference.UpdateTheme(ThemeKeys.Dark, null, "not-a-format", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("user_preferences.invalid_date_format", result.Error.Code);
    }
}
