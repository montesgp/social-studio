using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;

namespace SocialStudio.Infrastructure.Social.Providers.Apify;

/// <summary>
/// Platform adapter for Instagram via Apify actor shu8hvrXbJbY3Eb9W.
/// Field mappings verified against the live actor response (obs 426, 2026-05-23).
/// Key differences vs TikTok: no shares/saves; views only for Video type;
/// hashtags are plain strings (not objects); duration only for Video type.
/// Content type follows the same contentType-as-MediaReference pattern as TikTok.
/// </summary>
internal sealed class InstagramApifyScrapeAdapter : ISocialPlatformScrapeAdapter
{
    public string PlatformKey => "instagram";

    public object BuildActorInput(SocialContentRequest request) => new
    {
        resultsType = "posts",
        directUrls = new[] { request.NormalizedUrl },
        resultsLimit = 1,
        addParentData = false
    };

    public SocialContentSnapshot MapItem(JsonElement item, SocialContentRequest request)
    {
        var caption = GetString(item, "caption");
        var title = caption;

        // Author: ownerFullName takes priority; fall back to ownerUsername (obs 426)
        var author = GetString(item, "ownerFullName") ?? GetString(item, "ownerUsername");

        // Thumbnail from displayUrl
        var thumbnailUrl = GetString(item, "displayUrl");

        // Canonical URL from "url" field; fall back to request
        var canonicalUrl = GetString(item, "url") ?? request.NormalizedUrl;

        // ContentType: first-class field from "type" (Sidecar → carousel, Image → image, Video → video)
        var typeStr = GetString(item, "type") ?? string.Empty;
        var contentType = ResolveContentType(typeStr);
        var isVideo = typeStr.Equals("Video", StringComparison.OrdinalIgnoreCase);

        // Metrics: likes and comments always present; views only for video
        var metrics = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        TryAddMetric(metrics, item, "likes", "likesCount");
        TryAddMetric(metrics, item, "comments", "commentsCount");

        // Views for video: videoViewCount → fallback videoPlayCount
        if (isVideo)
        {
            if (!TryAddMetric(metrics, item, "views", "videoViewCount"))
            {
                TryAddMetric(metrics, item, "views", "videoPlayCount");
            }
        }

        // Duration: only for video; guard against JSON null literals
        int? duration = null;
        if (isVideo
            && item.TryGetProperty("videoDuration", out var durEl)
            && durEl.ValueKind != JsonValueKind.Null
            && durEl.TryGetInt32(out var durVal))
        {
            duration = durVal;
        }

        // Tags: hashtags as plain strings (obs 426 — IG hashtags are strings, not objects)
        var tags = ExtractHashtagStrings(item);

        // PublishedAtUtc: timestamp field (obs 426)
        DateTimeOffset? publishedAtUtc = null;
        var timestamp = GetString(item, "timestamp");
        if (!string.IsNullOrWhiteSpace(timestamp)
            && DateTimeOffset.TryParse(timestamp, out var parsed))
        {
            publishedAtUtc = parsed;
        }

        // Media references: image thumbnail + video (no content-type hack)
        var mediaReferences = new List<SocialMediaReference>();

        if (!string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            mediaReferences.Add(new SocialMediaReference("image", thumbnailUrl, "image/jpeg"));
        }

        var videoUrl = GetString(item, "videoUrl");
        if (isVideo && !string.IsNullOrWhiteSpace(videoUrl))
        {
            mediaReferences.Add(new SocialMediaReference("video", videoUrl, "video/mp4"));
        }

        // No transcript from Instagram actor — always add warning
        var warnings = new List<SocialContentWarning>
        {
            new("transcript_missing", "social.single_analyzer.warnings.transcript_missing")
        };

        return new SocialContentSnapshot(
            PlatformKey,
            canonicalUrl,
            title,
            caption,
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

    private static string ResolveContentType(string type) =>
        type.Equals("Video", StringComparison.OrdinalIgnoreCase)
            ? "video"
            : type.Equals("Sidecar", StringComparison.OrdinalIgnoreCase)
                ? "carousel"
                : "image";

    /// <summary>
    /// Extracts hashtags from the Instagram response where each element is a plain string.
    /// Format: hashtags: ["foo", "bar"] (obs 426 — IG hashtags are strings, not objects like TikTok).
    /// </summary>
    private static IReadOnlyList<string> ExtractHashtagStrings(JsonElement root)
    {
        if (!root.TryGetProperty("hashtags", out var hashtagsEl)
            || hashtagsEl.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
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

    private static bool TryAddMetric(
        Dictionary<string, long> metrics,
        JsonElement root,
        string metricKey,
        string jsonProperty)
    {
        if (!root.TryGetProperty(jsonProperty, out var value))
        {
            return false;
        }

        // Guard against JSON null literals (field present but null in the API response)
        if (value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (value.TryGetInt64(out var number))
        {
            metrics[metricKey] = number;
            return true;
        }

        return false;
    }

    private static string? GetString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
