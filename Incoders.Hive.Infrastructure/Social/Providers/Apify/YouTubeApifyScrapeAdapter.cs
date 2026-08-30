using System.Globalization;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;

namespace Incoders.Hive.Infrastructure.Social.Providers.Apify;

/// <summary>
/// Platform adapter for YouTube via the generic Apify actor streamers/youtube-scraper.
/// Field mappings verified against the live actor response (obs 2026-06-02) for a Shorts URL.
/// The same actor resolves both regular videos (type "video") and Shorts (type "shorts") from a
/// direct video URL, so a single adapter covers the whole platform — identical to the Instagram flow.
/// The channel-based streamers/youtube-shorts-scraper is deliberately NOT used: it requires a
/// "channels" input and cannot resolve an individual video by URL (rejected with HTTP 400
/// "Field input.channels is required"), which makes it unfit for single-content analysis.
/// </summary>
internal sealed class YouTubeApifyScrapeAdapter : ISocialPlatformScrapeAdapter
{
    public string PlatformKey => "youtube";

    public object BuildActorInput(SocialContentRequest request) => new
    {
        startUrls = new[] { new { url = request.NormalizedUrl } },
        // For a direct video URL the actor returns that single item; the limits are guards only.
        maxResults = 1,
        maxResultsShorts = 1,
        downloadSubtitles = false
    };

    public SocialContentSnapshot MapItem(JsonElement item, SocialContentRequest request)
    {
        var title = GetString(item, "title");
        var description = GetString(item, "text");

        // Author: channel display name
        var author = GetString(item, "channelName") ?? GetString(item, "channelUsername");

        var thumbnailUrl = GetString(item, "thumbnailUrl");

        // Canonical URL from "url"; fall back to request
        var canonicalUrl = GetString(item, "url") ?? request.NormalizedUrl;

        // ContentType: first-class "type" field ("shorts" or "video"); normalized to lowercase
        var contentType = GetString(item, "type")?.Trim().ToLowerInvariant();

        // Metrics: views/likes/comments always present for a public video
        var metrics = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        TryAddMetric(metrics, item, "views", "viewCount");
        TryAddMetric(metrics, item, "likes", "likes");
        TryAddMetric(metrics, item, "comments", "commentsCount");

        // Duration: "HH:MM:SS" or "MM:SS" string → total seconds
        var duration = ParseDuration(GetString(item, "duration"));

        // Tags: YouTube returns hashtag links in descriptionLinks; the "hashtags" array is usually empty.
        var tags = ExtractHashtags(item);

        // PublishedAtUtc: ISO 8601 "date" field
        DateTimeOffset? publishedAtUtc = null;
        var date = GetString(item, "date");
        if (!string.IsNullOrWhiteSpace(date)
            && DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            publishedAtUtc = parsed;
        }

        // Media references: image thumbnail + the canonical video page URL (no direct media file is exposed).
        var mediaReferences = new List<SocialMediaReference>();
        if (!string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            mediaReferences.Add(new SocialMediaReference("image", thumbnailUrl, "image/jpeg"));
        }

        mediaReferences.Add(new SocialMediaReference("video", canonicalUrl, "video/mp4"));

        // No transcript requested from the actor — always add the warning (same as Instagram/TikTok).
        var warnings = new List<SocialContentWarning>
        {
            new("transcript_missing", "social.single_analyzer.warnings.transcript_missing")
        };

        return new SocialContentSnapshot(
            PlatformKey,
            canonicalUrl,
            title,
            description,
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
    /// Parses a YouTube duration string ("HH:MM:SS" or "MM:SS") into total seconds.
    /// Returns null when the value is missing or not parseable.
    /// </summary>
    private static int? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        var total = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var segment))
            {
                return null;
            }

            total = (total * 60) + segment;
        }

        return total;
    }

    /// <summary>
    /// Extracts hashtags from the YouTube response. Primary source is descriptionLinks entries that
    /// point at "/hashtag/" (their "text" is like "#Foo"); falls back to the "hashtags" string array.
    /// The leading '#' is stripped to keep tags consistent with the other platform adapters.
    /// </summary>
    private static IReadOnlyList<string> ExtractHashtags(JsonElement root)
    {
        var tags = new List<string>();

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("descriptionLinks", out var linksEl)
            && linksEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in linksEl.EnumerateArray())
            {
                if (link.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var url = GetString(link, "url");
                var text = GetString(link, "text");
                if (url is null
                    || !url.Contains("/hashtag/", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                tags.Add(text.TrimStart('#'));
            }
        }

        if (tags.Count == 0
            && root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("hashtags", out var hashtagsEl)
            && hashtagsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in hashtagsEl.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var val = el.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        tags.Add(val.TrimStart('#'));
                    }
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
        if (!root.TryGetProperty(jsonProperty, out var value) || value.ValueKind == JsonValueKind.Null)
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
