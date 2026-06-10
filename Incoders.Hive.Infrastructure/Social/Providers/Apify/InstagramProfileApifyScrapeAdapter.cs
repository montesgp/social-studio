using System.Text.Json;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social.Providers.Apify;

/// <summary>
/// Maps Apify instagram-profile-scraper actor responses to domain value objects.
///
/// KEY BEHAVIORS (all confirmed from WU-01 spike / obs #513):
/// - The actor returns a 3-item array: [profile-result, quality_report, bandwidth_report].
///   App MUST select the item where resultType == "profile" (NOT positional index [0]) — PA-06.
/// - latestReels[] is always empty in instagram-profile-scraper mode.
///   Reels shared to feed appear in latestPosts[] with mediaType == "Video".
///   If latestReels[] is non-empty (unexpected), a Warning is logged per WU-01.
/// - videoViewCount is populated for Video posts; videoPlayCount is ALWAYS null in profile-scraper
///   mode — do NOT use videoPlayCount for median-views computation.
/// - videoDuration is also always null in profile-scraper mode.
/// - The owner-filter (ownerId == profile.instagramId) is applied DOWNSTREAM in ProfileMetricCalculator.
///   This adapter maps ALL posts raw so the calculator can apply the filter (PA-07).
///
/// OPERATOR SETUP NOTE:
/// The actor_mappings_json credential MUST contain TWO entries for (networkKey="instagram", variantKey="profile"):
///   [0] default:  { "networkKey": "instagram", "variantKey": "profile", "actorId": "get-leads~all-in-one-instagram-scraper" }
///   [1] backup:   { "networkKey": "instagram", "variantKey": "profile", "actorId": "apify~instagram-profile-scraper" }
/// Array order is preserved (List&lt;T&gt; deserialisation is deterministic); NO priority field needed.
/// </summary>
internal sealed class InstagramProfileApifyScrapeAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<InstagramProfileApifyScrapeAdapter> _logger;

    public InstagramProfileApifyScrapeAdapter(
        ILogger<InstagramProfileApifyScrapeAdapter>? logger = null)
    {
        _logger = logger ?? NullLogger<InstagramProfileApifyScrapeAdapter>.Instance;
    }

    // ── Input builder ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the default get-leads all-in-one profile actor input.
    /// Kept for existing unit tests and call sites; production code should prefer the
    /// actor-aware overload because Apify profile actors do not share one input schema.
    /// </summary>
    public object BuildProfileActorInput(string username, int maxPostsPerProfile = 12) => new
    {
        scrapeMode = "instagram-profile-scraper",
        profiles = new[] { username },
        maxPostsPerProfile,
        proxyTier = "residential"
    };

    /// <summary>
    /// Builds the Apify actor input using the schema required by the selected actor.
    /// </summary>
    /// <remarks>
    /// The get-leads all-in-one actor uses <c>profiles[]</c> plus <c>scrapeMode</c>.
    /// The official <c>apify/instagram-profile-scraper</c> actor uses <c>usernames[]</c>.
    /// Mixing these schemas is not harmless: the official actor returns HTTP 400, and
    /// get-leads emits an input-validation dataset item instead of a profile result.
    /// </remarks>
    public object BuildProfileActorInput(
        string actorId,
        string username,
        int maxPostsPerProfile = 12)
    {
        if (IsOfficialApifyProfileActor(actorId))
        {
            return new
            {
                usernames = new[] { username },
                includeAboutSection = false
            };
        }

        return BuildProfileActorInput(username, maxPostsPerProfile);
    }

    private static bool IsOfficialApifyProfileActor(string actorId)
    {
        var normalized = actorId.Trim().Replace('~', '/');
        return string.Equals(
            normalized,
            "apify/instagram-profile-scraper",
            StringComparison.OrdinalIgnoreCase);
    }

    // ── Response mapping ──────────────────────────────────────────────────────

    /// <summary>
    /// Maps the raw Apify response JSON to a <see cref="ProfileMappingResult"/>.
    /// Returns <c>null</c> when no item with <c>resultType == "profile"</c> is found (PA-06).
    /// The result carries ALL posts raw — owner filtering is the caller's responsibility (PA-07).
    /// </summary>
    public ProfileMappingResult? MapProfileResponse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            if (LooksLikeProfileResult(doc.RootElement))
            {
                var (rootResult, _) = MapProfileElement(doc.RootElement, warnOnNonEmptyReels: false);
                return rootResult;
            }

            return null;
        }

        // PA-06: Prefer resultType == "profile", NOT positional index [0].
        // The official apify/instagram-profile-scraper returns direct profile objects
        // without resultType, so fall back to the first object that is profile-shaped.
        JsonElement? profileElement = null;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var resultType = GetString(item, "resultType");
            if (string.Equals(resultType, "profile", StringComparison.OrdinalIgnoreCase))
            {
                profileElement = item;
                break;
            }
        }

        if (!profileElement.HasValue)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (LooksLikeProfileResult(item))
                {
                    profileElement = item;
                    break;
                }
            }
        }

        if (!profileElement.HasValue)
        {
            return null;
        }

        var el = profileElement.Value;
        var (result, _) = MapProfileElement(el, warnOnNonEmptyReels: false);
        return result;
    }

    /// <summary>
    /// Maps the raw response JSON and returns a flag indicating whether a Warning
    /// was emitted because <c>latestReels[]</c> was non-empty.
    /// Used for testability only; production code should call <see cref="MapProfileResponse"/>.
    /// </summary>
    public (ProfileMappingResult? Result, bool WarningEmitted) MapProfileResponseWithWarningCapture(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            if (LooksLikeProfileResult(doc.RootElement))
            {
                return MapProfileElement(doc.RootElement, warnOnNonEmptyReels: true);
            }

            return (null, false);
        }

        JsonElement? profileElement = null;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var resultType = GetString(item, "resultType");
            if (string.Equals(resultType, "profile", StringComparison.OrdinalIgnoreCase))
            {
                profileElement = item;
                break;
            }
        }

        if (!profileElement.HasValue)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (LooksLikeProfileResult(item))
                {
                    profileElement = item;
                    break;
                }
            }
        }

        if (!profileElement.HasValue)
        {
            return (null, false);
        }

        return MapProfileElement(profileElement.Value, warnOnNonEmptyReels: true);
    }

    // ── Static username helper (public for tests and resolver) ────────────────

    /// <summary>
    /// Parses an Instagram username from a profile URL, @handle, or bare handle.
    /// Examples: "https://www.instagram.com/testbrand/" → "testbrand"; "@testbrand" → "testbrand".
    /// </summary>
    public static string ParseUsername(string urlOrHandle)
    {
        if (string.IsNullOrWhiteSpace(urlOrHandle))
        {
            return string.Empty;
        }

        var trimmed = urlOrHandle.Trim();

        // Strip leading '@'
        if (trimmed.StartsWith('@'))
        {
            return trimmed[1..].Trim('/');
        }

        // Try to parse as URL and extract the first path segment
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Host.Contains("instagram.com", StringComparison.OrdinalIgnoreCase)))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                return segments[0].Trim('/');
            }
        }

        // Bare handle (no @, no URL)
        return trimmed.Trim('/');
    }

    // ── Private mapping logic ─────────────────────────────────────────────────

    private (ProfileMappingResult? Result, bool WarningEmitted) MapProfileElement(
        JsonElement el,
        bool warnOnNonEmptyReels)
    {
        var snapshot = MapSnapshot(el);
        var allPosts = MapPosts(el, "latestPosts", snapshot.InstagramId, snapshot.Username);
        var (reels, warningEmitted) = MapReels(
            el,
            warnOnNonEmptyReels,
            snapshot.InstagramId,
            snapshot.Username);
        var aggregates = MapAggregates(el, allPosts.Count, allPosts.Count, 0);

        return (new ProfileMappingResult(snapshot, allPosts, reels, aggregates), warningEmitted);
    }

    private static ProfileSnapshot MapSnapshot(JsonElement el) => new(
        InstagramId: GetString(el, "instagramId") ?? GetString(el, "id") ?? string.Empty,
        Username: GetString(el, "username") ?? string.Empty,
        FollowersCount: GetLong(el, "followersCount") ?? 0L,
        PostsCount: GetLong(el, "postsCount") ?? 0L,
        ReelsCount: GetLong(el, "reelsCount"),
        EngagementRate: GetDouble(el, "engagementRate") ?? 0.0,
        MedianDaysBetweenPosts: GetDouble(el, "medianDaysBetweenPosts"),
        ProfilePicUrl: GetString(el, "profilePicUrl"),
        Biography: GetString(el, "biography"),
        IsVerified: GetBool(el, "isVerified") || GetBool(el, "verified"),
        IsBusinessAccount: GetBool(el, "isBusinessAccount"),
        BusinessCategoryName: GetString(el, "businessCategoryName"));

    private static IReadOnlyList<ProfilePost> MapPosts(
        JsonElement el,
        string arrayKey,
        string profileInstagramId,
        string profileUsername)
    {
        if (!el.TryGetProperty(arrayKey, out var postsEl)
            || postsEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var posts = new List<ProfilePost>();
        foreach (var postEl in postsEl.EnumerateArray())
        {
            var post = MapSinglePost(postEl, profileInstagramId, profileUsername);
            if (post is not null)
            {
                posts.Add(post);
            }
        }

        return posts;
    }

    private (IReadOnlyList<ProfilePost> Reels, bool WarningEmitted) MapReels(
        JsonElement el,
        bool warnOnNonEmptyReels,
        string profileInstagramId,
        string profileUsername)
    {
        if (!el.TryGetProperty("latestReels", out var reelsEl)
            || reelsEl.ValueKind != JsonValueKind.Array
            || reelsEl.GetArrayLength() == 0)
        {
            return ([], false);
        }

        // Per WU-01 spike: latestReels[] is always empty in instagram-profile-scraper mode.
        // Reels-on-feed appear in latestPosts[] as mediaType == "Video".
        // If non-empty, log a Warning and still map defensively (same shape as latestPosts).
        if (warnOnNonEmptyReels)
        {
            _logger.LogWarning(
                "latestReels[] contains {Count} items — unexpected in instagram-profile-scraper mode (WU-01 spike finding: always empty). " +
                "Mapping defensively. Reels-on-feed already appear in latestPosts[] as Video.",
                reelsEl.GetArrayLength());
        }

        var reels = new List<ProfilePost>();
        foreach (var reelEl in reelsEl.EnumerateArray())
        {
            var reel = MapSinglePost(reelEl, profileInstagramId, profileUsername);
            if (reel is not null)
            {
                reels.Add(reel);
            }
        }

        return (reels, warnOnNonEmptyReels && reels.Count > 0);
    }

    private static ProfilePost? MapSinglePost(
        JsonElement postEl,
        string profileInstagramId,
        string profileUsername)
    {
        var postId = GetString(postEl, "postId")
            ?? GetString(postEl, "id")
            ?? GetString(postEl, "shortCode")
            ?? string.Empty;
        var mediaType = GetString(postEl, "mediaType") ?? GetString(postEl, "type") ?? "Image";
        var ownerId = GetString(postEl, "ownerId") ?? string.Empty;
        var ownerUsername = GetString(postEl, "ownerUsername") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerId)
            && !string.IsNullOrWhiteSpace(profileInstagramId)
            && !string.IsNullOrWhiteSpace(ownerUsername)
            && string.Equals(ownerUsername, profileUsername, StringComparison.OrdinalIgnoreCase))
        {
            ownerId = profileInstagramId;
        }

        // Timestamp — parse ISO 8601
        DateTimeOffset timestamp = default;
        var timestampStr = GetString(postEl, "timestamp");
        if (!string.IsNullOrWhiteSpace(timestampStr)
            && DateTimeOffset.TryParse(timestampStr, out var parsed))
        {
            timestamp = parsed;
        }

        // videoViewCount: populated for Video posts; null for Image/Carousel.
        // NOTE: videoPlayCount is ALWAYS null in profile-scraper mode (WU-01 spike obs #513).
        // Do NOT use videoPlayCount — only videoViewCount is reliable.
        long? videoViewCount = null;
        if (postEl.TryGetProperty("videoViewCount", out var vvcEl)
            && vvcEl.ValueKind != JsonValueKind.Null
            && vvcEl.TryGetInt64(out var vvc))
        {
            videoViewCount = vvc;
        }

        // _degraded flag (data quality marker from Apify actor)
        var isDegraded = false;
        if (postEl.TryGetProperty("_degraded", out var degradedEl)
            && degradedEl.ValueKind == JsonValueKind.True)
        {
            isDegraded = true;
        }

        // hashtags: plain strings for Instagram (obs #513, same as obs 426 for single-video)
        var hashtags = ExtractHashtagStrings(postEl);

        return new ProfilePost(
            PostId: string.IsNullOrWhiteSpace(postId) ? Guid.NewGuid().ToString() : postId,
            MediaType: mediaType,
            LikesCount: GetLong(postEl, "likesCount") ?? 0L,
            CommentsCount: GetLong(postEl, "commentsCount") ?? 0L,
            VideoViewCount: videoViewCount,
            Timestamp: timestamp,
            OwnerId: ownerId,
            OwnerUsername: ownerUsername,
            IsPaidPartnership: GetBool(postEl, "isPaidPartnership"),
            IsDegraded: isDegraded,
            Hashtags: hashtags);
    }

    private static ProfileAggregatesRaw MapAggregates(
        JsonElement el,
        int postsReceived,
        int postsProcessed,
        int degradedPostCount) => new(
        EngagementRate: GetDouble(el, "engagementRate") ?? 0.0,
        AvgLikes: GetDouble(el, "avgLikes") ?? 0.0,
        AvgComments: GetDouble(el, "avgComments") ?? 0.0,
        MedianDaysBetweenPosts: GetDouble(el, "medianDaysBetweenPosts"),
        FollowersCount: GetLong(el, "followersCount") ?? 0L,
        FollowingCount: GetLong(el, "followsCount") ?? 0L,
        PostsReceived: postsReceived,
        PostsProcessed: postsProcessed,
        DegradedPostCount: degradedPostCount);

    // ── JSON helpers ──────────────────────────────────────────────────────────

    private static IReadOnlyList<string> ExtractHashtagStrings(JsonElement root)
    {
        if (!root.TryGetProperty("hashtags", out var hashtagsEl)
            || hashtagsEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tags = new List<string>();
        foreach (var el in hashtagsEl.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var val = el.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    tags.Add(val);
                }
            }
        }

        return tags;
    }

    private static string? GetString(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(key, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static long? GetLong(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (v.TryGetInt64(out var i64))
        {
            return i64;
        }

        // Also accept floating point numbers in integer fields (some actors return 150000.0)
        if (v.TryGetDouble(out var d))
        {
            return (long)d;
        }

        return null;
    }

    private static double? GetDouble(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return v.TryGetDouble(out var d) ? d : null;
    }

    private static bool GetBool(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static bool LooksLikeProfileResult(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var resultType = GetString(el, "resultType");
        if (!string.IsNullOrWhiteSpace(resultType)
            && !string.Equals(resultType, "profile", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hasUsername = !string.IsNullOrWhiteSpace(GetString(el, "username"));
        var hasProfileIdentity =
            !string.IsNullOrWhiteSpace(GetString(el, "instagramId"))
            || !string.IsNullOrWhiteSpace(GetString(el, "id"));
        var hasProfileMetrics =
            el.TryGetProperty("followersCount", out _)
            || el.TryGetProperty("postsCount", out _)
            || el.TryGetProperty("latestPosts", out _);

        return hasUsername && hasProfileIdentity && hasProfileMetrics;
    }
}

/// <summary>
/// Intermediate mapping result from the adapter; carries raw (unfiltered) post data.
/// Owner filtering and degrade exclusion are applied downstream in ProfileMetricCalculator.
/// </summary>
internal sealed record ProfileMappingResult(
    ProfileSnapshot Profile,
    IReadOnlyList<ProfilePost> AllPosts,
    IReadOnlyList<ProfilePost> Reels,
    ProfileAggregatesRaw Aggregates);

/// <summary>
/// Raw aggregate fields from the Apify profile actor response, before owner/degrade filtering.
/// These are carried for diagnostics — not used in metric computation (which uses raw posts).
/// </summary>
internal sealed record ProfileAggregatesRaw(
    double EngagementRate,
    double AvgLikes,
    double AvgComments,
    double? MedianDaysBetweenPosts,
    long FollowersCount,
    long FollowingCount,
    int PostsReceived,
    int PostsProcessed,
    int DegradedPostCount);
