using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.UserPreferences;

public static class UserPreferenceErrors
{
    public static readonly Error UserRequired = Error.Validation(
        "user_preferences.user_required",
        "user_preferences.errors.user_required");

    public static readonly Error InvalidThemeKey = Error.Validation(
        "user_preferences.invalid_theme_key",
        "user_preferences.errors.invalid_theme_key");

    public static readonly Error AccentThemeTooLong = Error.Validation(
        "user_preferences.accent_theme_too_long",
        "user_preferences.errors.accent_theme_too_long");
}
