using Incoders.Hive.Domain.UserPreferences;

namespace Incoders.Hive.Application.Features.UserPreferences;

public sealed record UserPreferenceResponse(
    Guid? Id,
    Guid UserId,
    string ThemeKey,
    string? AccentTheme,
    string DateFormat,
    DateTime? CreatedAtUtc,
    DateTime? UpdatedAtUtc)
{
    public static UserPreferenceResponse FromDomain(UserPreference preference) =>
        new(
            preference.Id.Value,
            preference.UserId,
            preference.ThemeKey,
            preference.AccentTheme,
            preference.DateFormat,
            preference.CreatedAtUtc,
            preference.UpdatedAtUtc);
}
