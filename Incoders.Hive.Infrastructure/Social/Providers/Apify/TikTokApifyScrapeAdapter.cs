using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;

namespace Incoders.Hive.Infrastructure.Social.Providers.Apify;

/// <summary>
/// Platform adapter for TikTok via Apify actor GdWCkxBtKWOsKjdch.
/// Field names verified against the live actor response (obs 426, 2026-05-23).
/// </summary>
internal sealed class TikTokApifyScrapeAdapter : ISocialPlatformScrapeAdapter
{
    public string PlatformKey => "tiktok";

    public object BuildActorInput(SocialContentRequest request) => new
    {
        postURLs = new[] { request.NormalizedUrl },
        resultsPerPage = 1,
        shouldDownloadVideos = false,
        shouldDownloadCovers = false,
        shouldDownloadSlideshowImages = false,
        shouldDownloadAvatars = false,
        shouldDownloadMusicCovers = false,
        shouldDownloadSubtitles = false,
        proxyCountryCode = "None"
    };

    public SocialContentSnapshot MapItem(JsonElement item, SocialContentRequest request)
    {
        var title = GetString(item, "text");

        // authorMeta.nickName || authorMeta.name
        var author = GetNestedString(item, "authorMeta", "nickName")
            ?? GetNestedString(item, "authorMeta", "name");

        // videoMeta.coverUrl || videoMeta.originalCoverUrl || authorMeta.avatar
        var thumbnailUrl = GetNestedString(item, "videoMeta", "coverUrl")
            ?? GetNestedString(item, "videoMeta", "originalCoverUrl")
            ?? GetNestedString(item, "authorMeta", "avatar");

        var duration = GetNestedInt(item, "videoMeta", "duration");

        // Canonical URL: webVideoUrl or fall back to input
        var canonicalUrl = GetString(item, "webVideoUrl") ?? request.NormalizedUrl;

        // Metrics (verified field names)
        var metrics = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        TryAddMetric(metrics, item, "views", "playCount");
        TryAddMetric(metrics, item, "likes", "diggCount");
        TryAddMetric(metrics, item, "comments", "commentCount");
        TryAddMetric(metrics, item, "shares", "shareCount");
        TryAddMetric(metrics, item, "saves", "collectCount");

        // ContentType: first-class field from isSlideshow (obs 426)
        var isSlideshow = item.TryGetProperty("isSlideshow", out var isSlideshowEl)
            && isSlideshowEl.ValueKind == JsonValueKind.True;
        var contentType = isSlideshow ? "slideshow" : "video";

        // Tags: hashtags[].name (TikTok hashtags are objects, not strings — obs 426)
        var tags = ExtractHashtagNames(item);

        // PublishedAtUtc: createTimeISO (obs 426)
        DateTimeOffset? publishedAtUtc = null;
        var createTimeIso = GetString(item, "createTimeISO");
        if (!string.IsNullOrWhiteSpace(createTimeIso)
            && DateTimeOffset.TryParse(createTimeIso, out var parsed))
        {
            publishedAtUtc = parsed;
        }

        // Media references: video only (no content-type hack)
        var mediaReferences = new List<SocialMediaReference>();
        var webVideoUrl = GetString(item, "webVideoUrl");
        if (!string.IsNullOrWhiteSpace(webVideoUrl))
        {
            mediaReferences.Add(new SocialMediaReference("video", webVideoUrl, "video/mp4"));
        }

        // No transcript from TikTok actor — always add warning
        var warnings = new List<SocialContentWarning>
        {
            new("transcript_missing", "social.single_analyzer.warnings.transcript_missing")
        };

        return new SocialContentSnapshot(
            PlatformKey,
            canonicalUrl,
            title,
            title,
            author,
            thumbnailUrl,
            duration,
            metrics,
            Transcript: null,
            mediaReferences,
            RawPayloadReference: "provider://apify/run-sync",
            warnings,
            Tags: tags,
            ContentType: contentType,
            PublishedAtUtc: publishedAtUtc);
    }

    /// <summary>
    /// Extracts hashtag names from the TikTok response array where each element is an object with a "name" property.
    /// Format: hashtags: [{ "id": "1", "name": "foo", "title": "foo" }, ...]
    /// </summary>
    private static IReadOnlyList<string> ExtractHashtagNames(JsonElement root)
    {
        if (!root.TryGetProperty("hashtags", out var hashtagsEl)
            || hashtagsEl.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (var tag in hashtagsEl.EnumerateArray())
        {
            if (tag.ValueKind == JsonValueKind.Object
                && tag.TryGetProperty("name", out var nameEl)
                && nameEl.ValueKind == JsonValueKind.String)
            {
                var name = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static void TryAddMetric(
        Dictionary<string, long> metrics,
        JsonElement root,
        string metricKey,
        string jsonProperty)
    {
        if (root.TryGetProperty(jsonProperty, out var value) && value.TryGetInt64(out var number))
        {
            metrics[metricKey] = number;
        }
    }

    private static string? GetString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNestedString(JsonElement root, string parent, string child)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(parent, out var parentEl)
            || parentEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(parentEl, child);
    }

    private static int? GetNestedInt(JsonElement root, string parent, string child)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(parent, out var parentEl)
            || parentEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return parentEl.TryGetProperty(child, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;
    }
}
