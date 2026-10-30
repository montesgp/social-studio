using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.UserPreferences;

/// <summary>
/// User-owned visual preferences for module theming.
/// </summary>
public sealed class UserPreference : Entity<UserPreferenceId>
{
    public const int AccentThemeMaxLength = 64;

    private UserPreference(
        UserPreferenceId id,
        Guid userId,
        string themeKey,
        string? accentTheme,
        string dateFormat,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        UserId = userId;
        ThemeKey = themeKey;
        AccentTheme = accentTheme;
        DateFormat = dateFormat;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid UserId { get; private set; }

    public string ThemeKey { get; private set; }

    public string? AccentTheme { get; private set; }

    public string DateFormat { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<UserPreference> Create(
        UserPreferenceId id,
        Guid userId,
        string themeKey,
        string? accentTheme,
        string dateFormat,
        DateTime nowUtc)
    {
        var validation = Validate(userId, themeKey, accentTheme, dateFormat);
        if (validation.IsFailure)
        {
            return Result.Failure<UserPreference>(validation.Error);
        }

        return Result.Success(new UserPreference(
            id,
            userId,
            ThemeKeys.Normalize(themeKey),
            NormalizeAccentTheme(accentTheme),
            DateFormats.Normalize(dateFormat),
            nowUtc,
            nowUtc));
    }

    public static UserPreference Restore(
        UserPreferenceId id,
        Guid userId,
        string themeKey,
        string? accentTheme,
        string dateFormat,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new UserPreference(id, userId, themeKey, accentTheme, dateFormat, createdAtUtc, updatedAtUtc);
    }

    public Result UpdateTheme(string themeKey, string? accentTheme, string dateFormat, DateTime nowUtc)
    {
        var validation = Validate(UserId, themeKey, accentTheme, dateFormat);
        if (validation.IsFailure)
        {
            return validation;
        }

        ThemeKey = ThemeKeys.Normalize(themeKey);
        AccentTheme = NormalizeAccentTheme(accentTheme);
        DateFormat = DateFormats.Normalize(dateFormat);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private static Result Validate(Guid userId, string themeKey, string? accentTheme, string dateFormat)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(UserPreferenceErrors.UserRequired);
        }

        if (!ThemeKeys.IsSupported(themeKey))
        {
            return Result.Failure(UserPreferenceErrors.InvalidThemeKey);
        }

        var normalizedAccentTheme = NormalizeAccentTheme(accentTheme);
        if (normalizedAccentTheme?.Length > AccentThemeMaxLength)
        {
            return Result.Failure(UserPreferenceErrors.AccentThemeTooLong);
        }

        if (!DateFormats.IsSupported(dateFormat))
        {
            return Result.Failure(UserPreferenceErrors.InvalidDateFormat);
        }

        return Result.Success();
    }

    private static string? NormalizeAccentTheme(string? accentTheme) =>
        string.IsNullOrWhiteSpace(accentTheme) ? null : accentTheme.Trim();
}
