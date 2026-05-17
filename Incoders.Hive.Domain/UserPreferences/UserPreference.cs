using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.UserPreferences;

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
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        UserId = userId;
        ThemeKey = themeKey;
        AccentTheme = accentTheme;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid UserId { get; private set; }

    public string ThemeKey { get; private set; }

    public string? AccentTheme { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<UserPreference> Create(
        UserPreferenceId id,
        Guid userId,
        string themeKey,
        string? accentTheme,
        DateTime nowUtc)
    {
        var validation = Validate(userId, themeKey, accentTheme);
        if (validation.IsFailure)
        {
            return Result.Failure<UserPreference>(validation.Error);
        }

        return Result.Success(new UserPreference(
            id,
            userId,
            ThemeKeys.Normalize(themeKey),
            NormalizeAccentTheme(accentTheme),
            nowUtc,
            nowUtc));
    }

    public static UserPreference Restore(
        UserPreferenceId id,
        Guid userId,
        string themeKey,
        string? accentTheme,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new UserPreference(id, userId, themeKey, accentTheme, createdAtUtc, updatedAtUtc);
    }

    public Result UpdateTheme(string themeKey, string? accentTheme, DateTime nowUtc)
    {
        var validation = Validate(UserId, themeKey, accentTheme);
        if (validation.IsFailure)
        {
            return validation;
        }

        ThemeKey = ThemeKeys.Normalize(themeKey);
        AccentTheme = NormalizeAccentTheme(accentTheme);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    private static Result Validate(Guid userId, string themeKey, string? accentTheme)
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

        return Result.Success();
    }

    private static string? NormalizeAccentTheme(string? accentTheme) =>
        string.IsNullOrWhiteSpace(accentTheme) ? null : accentTheme.Trim();
}
