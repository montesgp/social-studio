using System.Globalization;
using System.Text.Json;
using System.Xml;
using SocialStudio.Application.Abstractions.Social.NicheDiscovery;

namespace SocialStudio.Infrastructure.Social.NicheDiscovery.Origins;

/// <summary>
/// The normalization boundary for the YouTube Data API v3 origin (design §5.4, ND-04): the ONLY type
/// that sees <see cref="JsonElement"/> for this origin. An item it cannot map is SKIPPED, never
/// defaulted; a metric the API does not report maps to <c>null</c>, NEVER <c>0</c> (ND-05).
///
/// PENDING LIVE VERIFICATION (mirrors ADR-0012's amendment for the Apify actor spike): this mapper is
/// built and unit-tested entirely against a recorded/handwritten fixture
/// (<c>Fixtures/youtube-niche-search.json</c>). No Google Cloud project with YouTube Data API v3 has
/// been provisioned yet — a final verification against a real response is deferred to the owner
/// provisioning that project.
/// </summary>
internal static class YouTubeEvidenceMapper
{
    internal static YouTubeVideoMappingResult MapVideos(JsonElement videosListResponse, string sourceKey)
    {
        var videos = new List<VideoEvidence>();
        var total = 0;

        if (videosListResponse.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                total++;
                var mapped = MapItem(item, sourceKey);
                if (mapped is not null)
                {
                    videos.Add(mapped);
                }
            }
        }

        return new YouTubeVideoMappingResult(videos, total, total - videos.Count);
    }

    internal static YouTubeChannelMappingResult MapChannels(JsonElement channelsListResponse, string sourceKey)
    {
        var channels = new List<ChannelEvidence>();
        var total = 0;

        if (channelsListResponse.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                total++;
                var mapped = MapChannelItem(item, sourceKey);
                if (mapped is not null)
                {
                    channels.Add(mapped);
                }
            }
        }

        return new YouTubeChannelMappingResult(channels, total, total - channels.Count);
    }

    /// <summary>
    /// Maps ONE videos.list item. An id or title missing means the item cannot be honestly represented
    /// — SKIPPED (returns null), never defaulted to an empty string or a fabricated title.
    /// </summary>
    internal static VideoEvidence? MapItem(JsonElement item, string sourceKey)
    {
        var externalRef = GetString(item, "id");
        if (string.IsNullOrWhiteSpace(externalRef))
        {
            return null;
        }

        if (!item.TryGetProperty("snippet", out var snippet) || snippet.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = GetString(snippet, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var channelExternalRef = GetString(snippet, "channelId") ?? string.Empty;
        var description = GetString(snippet, "description");
        var publishedAt = GetDateTimeOffset(snippet, "publishedAt");
        var languageCode = GetString(snippet, "defaultAudioLanguage") ?? GetString(snippet, "defaultLanguage");

        item.TryGetProperty("statistics", out var statistics);
        var views = GetLong(statistics, "viewCount");
        var likes = GetLong(statistics, "likeCount");
        var comments = GetLong(statistics, "commentCount");

        item.TryGetProperty("contentDetails", out var contentDetails);
        var durationSeconds = GetDurationSeconds(contentDetails);

        return new VideoEvidence(
            externalRef,
            channelExternalRef,
            title,
            description,
            publishedAt,
            views,
            likes,
            comments,
            durationSeconds,
            languageCode,
            sourceKey);
    }

    /// <summary>
    /// Maps ONE channels.list item. An id or title missing means the item cannot be honestly
    /// represented — SKIPPED, never defaulted.
    /// </summary>
    internal static ChannelEvidence? MapChannelItem(JsonElement item, string sourceKey)
    {
        var externalRef = GetString(item, "id");
        if (string.IsNullOrWhiteSpace(externalRef))
        {
            return null;
        }

        if (!item.TryGetProperty("snippet", out var snippet) || snippet.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var handle = GetString(snippet, "title");
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        var country = GetString(snippet, "country");
        var languageCode = GetString(snippet, "defaultLanguage");
        var createdAt = GetDateTimeOffset(snippet, "publishedAt");

        item.TryGetProperty("statistics", out var statistics);
        var subscriberCount = HasHiddenStatistics(statistics, "hiddenSubscriberCount")
            ? null
            : GetLong(statistics, "subscriberCount");
        var totalViews = GetLong(statistics, "viewCount");
        var videoCount = GetLong(statistics, "videoCount") is { } count ? (int)count : (int?)null;

        return new ChannelEvidence(
            externalRef,
            handle,
            country,
            subscriberCount,
            totalViews,
            videoCount,
            languageCode,
            createdAt,
            GetThumbnailUrl(snippet),
            GetString(snippet, "description"),
            sourceKey);
    }

    /// <summary>
    /// Best available avatar, largest first. The provider omits sizes for some channels, so each tier is
    /// probed rather than assumed; nothing is synthesised when none is present.
    /// </summary>
    private static string? GetThumbnailUrl(JsonElement snippet)
    {
        if (!snippet.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var size in (string[])["high", "medium", "default"])
        {
            if (thumbnails.TryGetProperty(size, out var tier)
                && tier.ValueKind == JsonValueKind.Object
                && GetString(tier, "url") is { } url)
            {
                return url;
            }
        }

        return null;
    }

    private static bool HasHiddenStatistics(JsonElement statistics, string flagName) =>
        statistics.ValueKind == JsonValueKind.Object
        && statistics.TryGetProperty(flagName, out var flag)
        && flag.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement parent, string propertyName)
    {
        var raw = GetString(parent, propertyName);
        return raw is not null
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// The YouTube Data API v3 reports every statistics counter as a STRING, and omits the property
    /// entirely rather than reporting zero when a channel hides it (absent = null, never 0, per ND-05).
    /// </summary>
    private static long? GetLong(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt64(out var numeric) => numeric,
            _ => null,
        };
    }

    private static int? GetDurationSeconds(JsonElement contentDetails)
    {
        var raw = GetString(contentDetails, "duration");
        if (raw is null)
        {
            return null;
        }

        try
        {
            return (int)XmlConvert.ToTimeSpan(raw).TotalSeconds;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

internal sealed record YouTubeVideoMappingResult(IReadOnlyList<VideoEvidence> Videos, int TotalItems, int SkippedCount);

internal sealed record YouTubeChannelMappingResult(IReadOnlyList<ChannelEvidence> Channels, int TotalItems, int SkippedCount);
