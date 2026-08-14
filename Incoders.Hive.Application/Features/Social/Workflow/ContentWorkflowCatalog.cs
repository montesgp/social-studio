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

    /// <summary>
    /// Where a run's raw material comes from. <c>discovery</c> reaches the market (scraping providers,
    /// or the stored candidate bank); <c>ai-ideas</c> reaches nothing at all and lets the model invent
    /// topics from the client's own context. This decides whether an actor can run, so an unrecognised
    /// value is rejected rather than defaulted — see ContentWorkflowErrors.OriginModeInvalid.
    /// </summary>
    private static readonly Dictionary<string, string> OriginModeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["discovery"] = "discovery",
        ["descubrimiento"] = "discovery",
        ["scraping"] = "discovery",
        ["ai-ideas"] = "ai-ideas",
        ["ai_ideas"] = "ai-ideas",
        ["ideas"] = "ai-ideas",
        ["ia"] = "ai-ideas",
        ["ideas-ia"] = "ai-ideas",
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

    /// <summary>The canonical value used when a caller says nothing: today's behaviour, unchanged.</summary>
    public const string DefaultOriginMode = "discovery";

    public static IReadOnlyList<string> OriginModes { get; } = OriginModeAliases.Values.Distinct().OrderBy(x => x).ToList();

    /// <summary>
    /// Normalizes an origin mode. An EMPTY or absent value resolves to <see cref="DefaultOriginMode"/>
    /// so every existing caller keeps working; anything unrecognised resolves to empty and the caller
    /// must reject it.
    /// </summary>
    public static string NormalizeOriginMode(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DefaultOriginMode
            : NormalizeAlias(value, OriginModeAliases);

    public static bool IsValidOriginMode(string? value) => NormalizeOriginMode(value).Length > 0;

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
