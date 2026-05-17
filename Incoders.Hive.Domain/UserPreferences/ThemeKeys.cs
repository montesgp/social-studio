namespace Incoders.Hive.Domain.UserPreferences;

public static class ThemeKeys
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    public static IReadOnlyCollection<string> All { get; } = [Dark, Light, System];

    public static bool IsSupported(string themeKey) =>
        !string.IsNullOrWhiteSpace(themeKey)
        && All.Contains(themeKey.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string themeKey)
    {
        var normalized = themeKey.Trim().ToLowerInvariant();
        return normalized switch
        {
            Dark => Dark,
            Light => Light,
            System => System,
            _ => normalized,
        };
    }
}
