using System.Net;
using System.Text.Json;
using System.Web;

namespace SocialStudio.Infrastructure.Social.NicheDiscovery.Origins;

/// <summary>
/// A named <see cref="IHttpClientFactory"/> client for the YouTube Data API v3 (design §5.3): "search
/// sparingly, batch everything else". <c>search.list</c> costs 100 units; <c>videos.list</c>/
/// <c>channels.list</c> cost 1 unit for up to 50 ids per call. The base address and API key are NEVER
/// literals here — both arrive resolved from <see cref="NicheDiscoveryOriginConfiguration"/>.
/// </summary>
internal sealed class YouTubeDataApiClient
{
    private const int SearchCost = 100;
    private const int ListCost = 1;
    private const int MaxIdsPerListCall = 50;

    private readonly IHttpClientFactory _httpClientFactory;

    // GAP FIX (WU-11b): PUBLIC constructor, even though the class itself stays `internal sealed`
    // (assembly-invisible either way). The DI container's default constructor resolution only
    // considers PUBLIC constructors; an `internal` one made `services.AddScoped<YouTubeDataApiClient>()`
    // (WU-5) throw "no suitable constructor" the first time anything actually resolved
    // INicheDiscoverySource through the real container — caught here by NicheDiscoveryCompositionTests,
    // the first test in this change to build AddInfrastructure and resolve that port.
    public YouTubeDataApiClient(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClientFactory = httpClientFactory;
    }

    internal static int SearchUnitCost => SearchCost;

    internal static int ListUnitCost => ListCost;

    internal static int MaxIdsPerBatch => MaxIdsPerListCall;

    /// <summary>
    /// <c>search.list?type=video&amp;order=viewCount&amp;maxResults=50&amp;publishedAfter=...</c> —
    /// harvests video ids AND channel ids from the same response, the ~1–2% cost strategy of §5.3.
    /// </summary>
    internal async Task<YouTubeApiCallResult> SearchVideosAsync(
        string baseAddress,
        string apiKey,
        string query,
        string? relevanceLanguage,
        DateTimeOffset publishedAfter,
        int maxResults,
        CancellationToken ct)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["part"] = "snippet",
            ["type"] = "video",
            ["order"] = "viewCount",
            ["maxResults"] = Math.Min(maxResults, 50).ToString(),
            ["q"] = query,
            ["publishedAfter"] = publishedAfter.ToUniversalTime().ToString("O"),
            ["relevance" + "Language"] = relevanceLanguage,
            ["key"] = apiKey,
        };

        return await SendAsync(baseAddress, "search", parameters, ct);
    }

    /// <summary>
    /// <c>videoCategories.list?regionCode=...</c> — the category taxonomy available in one region, so
    /// the sweep below can walk it instead of the operator naming a niche. Costs
    /// <see cref="ListUnitCost"/>.
    /// </summary>
    internal Task<YouTubeApiCallResult> ListVideoCategoriesAsync(
        string baseAddress, string apiKey, string regionCode, CancellationToken ct) =>
        SendAsync(
            baseAddress,
            "videoCategories",
            new Dictionary<string, string?>
            {
                ["part"] = "snippet",
                ["regionCode"] = regionCode,
                ["key"] = apiKey,
            },
            ct);

    /// <summary>
    /// <c>videos.list?chart=mostPopular&amp;regionCode=...&amp;videoCategoryId=...</c> — the provider's
    /// ONLY query-less feed, and therefore the only way to ask "what is performing" without naming it
    /// first. Costs <see cref="ListUnitCost"/> for up to <see cref="MaxIdsPerBatch"/> videos, versus
    /// <see cref="SearchUnitCost"/> for the same volume through search: two orders of magnitude cheaper
    /// per video, which is what makes sweeping a whole taxonomy affordable at all.
    ///
    /// <c>regionCode</c> is mandatory here — the chart is defined per region and the API rejects the
    /// call without it. That is a PROVIDER constraint on sampling, not a decision to analyse by region.
    /// </summary>
    internal Task<YouTubeApiCallResult> ListTrendingVideosAsync(
        string baseAddress,
        string apiKey,
        string regionCode,
        string? videoCategoryId,
        int maxResults,
        CancellationToken ct) =>
        SendAsync(
            baseAddress,
            "videos",
            new Dictionary<string, string?>
            {
                ["part"] = "snippet,statistics,contentDetails",
                ["chart"] = "mostPopular",
                ["regionCode"] = regionCode,
                ["videoCategoryId"] = videoCategoryId,
                ["maxResults"] = Math.Min(maxResults, MaxIdsPerListCall).ToString(),
                ["key"] = apiKey,
            },
            ct);

    internal Task<YouTubeApiCallResult> ListVideosAsync(
        string baseAddress, string apiKey, IReadOnlyList<string> videoIds, CancellationToken ct) =>
        SendAsync(
            baseAddress,
            "videos",
            new Dictionary<string, string?>
            {
                ["part"] = "snippet,statistics,contentDetails",
                ["id"] = string.Join(',', videoIds.Take(MaxIdsPerListCall)),
                ["key"] = apiKey,
            },
            ct);

    internal Task<YouTubeApiCallResult> ListChannelsAsync(
        string baseAddress, string apiKey, IReadOnlyList<string> channelIds, CancellationToken ct) =>
        SendAsync(
            baseAddress,
            "channels",
            new Dictionary<string, string?>
            {
                ["part"] = "snippet,statistics",
                ["id"] = string.Join(',', channelIds.Take(MaxIdsPerListCall)),
                ["key"] = apiKey,
            },
            ct);

    private async Task<YouTubeApiCallResult> SendAsync(
        string baseAddress,
        string relativePath,
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken ct)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        foreach (var (key, value) in parameters)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query[key] = value;
            }
        }

        var uri = $"{baseAddress.TrimEnd('/')}/{relativePath}?{query}";

        using var http = _httpClientFactory.CreateClient(nameof(YouTubeDataApiClient));

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(uri, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return YouTubeApiCallResult.Failure(isQuotaExceeded: false, HttpStatusCode.RequestTimeout);
        }
        catch (HttpRequestException)
        {
            return YouTubeApiCallResult.Failure(isQuotaExceeded: false, statusCode: null);
        }

        using var responseHandle = response;
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var isQuotaExceeded = response.StatusCode == HttpStatusCode.Forbidden
                && body.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase);

            return YouTubeApiCallResult.Failure(isQuotaExceeded, response.StatusCode);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(body);
            return YouTubeApiCallResult.Success(payload);
        }
        catch (JsonException)
        {
            return YouTubeApiCallResult.Failure(isQuotaExceeded: false, response.StatusCode);
        }
    }
}

internal sealed record YouTubeApiCallResult(bool IsSuccess, JsonElement? Payload, bool IsQuotaExceeded, HttpStatusCode? StatusCode)
{
    internal static YouTubeApiCallResult Success(JsonElement payload) => new(true, payload, false, HttpStatusCode.OK);

    internal static YouTubeApiCallResult Failure(bool isQuotaExceeded, HttpStatusCode? statusCode) =>
        new(false, null, isQuotaExceeded, statusCode);
}
