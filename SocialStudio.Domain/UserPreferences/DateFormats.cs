namespace SocialStudio.Domain.UserPreferences;

public static class DateFormats
{
    public const string DdMmYyyy = "dd/MM/yyyy";
    public const string MmDdYyyy = "MM/dd/yyyy";
    public const string YyyyMmDd = "yyyy-MM-dd";

    public const string Default = DdMmYyyy;

    public static IReadOnlyCollection<string> All { get; } = [DdMmYyyy, MmDdYyyy, YyyyMmDd];

    public static bool IsSupported(string dateFormat) =>
        !string.IsNullOrWhiteSpace(dateFormat)
        && All.Contains(dateFormat.Trim(), StringComparer.Ordinal);

    public static string Normalize(string dateFormat) => dateFormat.Trim();
}
