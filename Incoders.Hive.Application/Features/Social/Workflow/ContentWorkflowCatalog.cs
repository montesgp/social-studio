namespace Incoders.Hive.Application.Features.Social.Workflow;

public static class ContentWorkflowCatalog
{
    private static readonly Dictionary<string, string> SearchCriterionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["niche"] = "niche",
        ["nicho"] = "niche",
        ["references"] = "references",
        ["reference"] = "references",
        ["referentes"] = "references",
        ["own"] = "own",
        ["propios"] = "own",
        ["viral"] = "viral",
        ["virales"] = "viral",
        ["competitors"] = "competitors",
        ["competitor"] = "competitors",
        ["competidores"] = "competitors",
        ["competidor"] = "competitors",
    };

    private static readonly Dictionary<string, string> StrategyModeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mixed"] = "mixed",
        ["mix"] = "mixed",
        ["educational"] = "educational",
        ["educativo"] = "educational",
        ["informative"] = "informative",
        ["informativo"] = "informative",
        ["entertainment"] = "entertainment",
        ["entretenimiento"] = "entertainment",
    };

    private static readonly Dictionary<string, string> NetworkAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tiktok"] = "tiktok",
        ["tik tok"] = "tiktok",
        ["instagram"] = "instagram",
        ["ig"] = "instagram",
        ["youtube"] = "youtube",
        ["yt"] = "youtube",
    };

    private static readonly Dictionary<string, string> ActionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["download_audio"] = "download_audio",
        ["descargar_audio"] = "download_audio",
        ["transcribe_and_build_script"] = "transcribe_and_build_script",
        ["transcribe_script"] = "transcribe_and_build_script",
        ["transcribir_y_armar_guion"] = "transcribe_and_build_script",
        ["build_carousel"] = "build_carousel",
        ["carousel"] = "build_carousel",
        ["armar_carousel"] = "build_carousel",
    };

    public static IReadOnlyList<string> SearchCriteria { get; } = SearchCriterionAliases.Values.Distinct().OrderBy(x => x).ToList();

    public static IReadOnlyList<string> StrategyModes { get; } = StrategyModeAliases.Values.Distinct().OrderBy(x => x).ToList();

    public static IReadOnlyList<string> NetworkSources { get; } = NetworkAliases.Values.Distinct().OrderBy(x => x).ToList();

    public static IReadOnlyList<string> ActionTypes { get; } = ActionAliases.Values.Distinct().OrderBy(x => x).ToList();

    public static string NormalizeToken(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

    public static string NormalizeSearchCriterion(string value) => NormalizeAlias(value, SearchCriterionAliases);

    public static bool IsValidSearchCriterion(string value) => NormalizeSearchCriterion(value).Length > 0;

    public static string NormalizeStrategyMode(string value) => NormalizeAlias(value, StrategyModeAliases);

    public static bool IsValidStrategyMode(string value) => NormalizeStrategyMode(value).Length > 0;

    public static string NormalizeNetworkSource(string value) => NormalizeAlias(value, NetworkAliases);

    public static bool IsValidNetworkSource(string value) => NormalizeNetworkSource(value).Length > 0;

    public static IReadOnlyList<string> NormalizeNetworkSources(IEnumerable<string>? values) =>
        (values ?? Array.Empty<string>())
            .Select(NormalizeNetworkSource)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

    public static string NormalizeActionType(string value) => NormalizeAlias(value, ActionAliases);

    public static bool IsValidActionType(string value) => NormalizeActionType(value).Length > 0;

    private static string NormalizeAlias(string value, IReadOnlyDictionary<string, string> aliases)
    {
        var normalized = NormalizeToken(value);
        return normalized.Length > 0 && aliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : string.Empty;
    }
}
