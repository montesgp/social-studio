using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

namespace Incoders.Hive.Infrastructure.Social.Providers.Apify;

/// <summary>
/// Maps a TikTok niche-search Apify actor response into <see cref="DiscoveredContent"/>.
///
/// PENDING LIVE VERIFICATION — DOCUMENTATION-DERIVED (owner-waived spike, 2026-08-10). No configured
/// niche-search actor was available to call at the time this was written, so the field mapping below is
/// the DOCUMENTED TikTok Apify item shape, i.e. the same field family already verified live for the
/// single-video flow (<see cref="TikTokApifyScrapeAdapter"/>, obs 426). Every metric stays nullable and
/// every absent field degrades to <c>null</c> rather than throwing or being zero-filled, so the FIRST
/// real call against a configured actor only needs to correct field names in <see cref="MapItem"/> — one
/// obvious place — not the discovery chain, the scorer or the classifier.
/// </summary>
public sealed class TikTokNicheSearchAdapter
{
    private const string NetworkKey = "tiktok";
    private const string SourceKey = "niche";

    /// <summary>
    /// Input shape to try first against the configured actor: keyword search. If the configured actor
    /// does not serve search, the resolver retries with a hashtag-shaped input instead (design §7).
    /// </summary>
    public object BuildSearchQueriesInput(string searchTerm, int resultsPerPage) => new
    {
        searchQueries = new[] { searchTerm },
        resultsPerPage,
        shouldDownloadVideos = false,
        shouldDownloadCovers = false,
        shouldDownloadSlideshowImages = false,
        shouldDownloadAvatars = false,
        shouldDownloadMusicCovers = false,
        shouldDownloadSubtitles = false,
        proxyCountryCode = "None"
    };

    public object BuildHashtagsInput(string hashtag, int resultsPerPage) => new
    {
        hashtags = new[] { hashtag },
        resultsPerPage,
        shouldDownloadVideos = false,
        shouldDownloadCovers = false,
        shouldDownloadSlideshowImages = false,
        shouldDownloadAvatars = false,
        shouldDownloadMusicCovers = false,
        shouldDownloadSubtitles = false,
        proxyCountryCode = "None"
    };

    /// <summary>
    /// Maps every item in the response array. An item that cannot be mapped is SKIPPED, never
    /// defaulted — never a candidate with an invented URL, a zero-filled metric or a fabricated handle
    /// (design §7.1). Returns the mapped candidates plus the total and skipped counts so the caller can
    /// apply the "zero-of-many mapped → Failed / partial → Ran" rule without re-counting.
    /// </summary>
    public (IReadOnlyList<DiscoveredContent> Mapped, int TotalCount, int SkippedCount) MapItems(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array)
        {
            return ([], 0, 0);
        }

        var mapped = new List<DiscoveredContent>();
        var total = 0;

        foreach (var item in items.EnumerateArray())
        {
            total++;

            DiscoveredContent? candidate;
            try
            {
                candidate = MapItem(item);
            }
            catch (JsonException)
            {
                // The adapter never throws on shape — a malformed item is skipped, not fatal to the run.
                candidate = null;
            }

            if (candidate is not null)
            {
                mapped.Add(candidate);
            }
        }

        return (mapped, total, total - mapped.Count);
    }

    /// <summary>
    /// Maps a single item, or returns <c>null</c> when the item cannot be mapped (no canonical URL, or
    /// a malformed shape). Never throws — a <see cref="JsonException"/> reading a nested property is
    /// caught and treated as "unmappable", not fatal.
    /// </summary>
    public DiscoveredContent? MapItem(JsonElement item)
    {
        try
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Canonical URL is required — an item with no webVideoUrl carries nothing an operator or
            // the downstream Single Analyzer could act on, so it is skipped rather than defaulted.
            var contentUrl = GetString(item, "webVideoUrl");
            if (string.IsNullOrWhiteSpace(contentUrl))
            {
                return null;
            }

            var text = GetString(item, "text") ?? string.Empty;

            var handle = GetNestedString(item, "authorMeta", "nickName")
                ?? GetNestedString(item, "authorMeta", "name");
            if (string.IsNullOrWhiteSpace(handle))
            {
                return null;
            }

            var thumbnailUrl = GetNestedString(item, "videoMeta", "coverUrl")
                ?? GetNestedString(item, "videoMeta", "originalCoverUrl")
                ?? GetNestedString(item, "authorMeta", "avatar");

            DateTimeOffset? publishedAtUtc = null;
            var createTimeIso = GetString(item, "createTimeISO");
            if (!string.IsNullOrWhiteSpace(createTimeIso)
                && DateTimeOffset.TryParse(createTimeIso, out var parsed))
            {
                publishedAtUtc = parsed;
            }

            return new DiscoveredContent(
                Title: FirstLine(text, handle),
                Summary: text,
                NetworkSource: NetworkKey,
                ContentUrl: contentUrl,
                ThumbnailUrl: thumbnailUrl,
                Views: GetLong(item, "playCount"),
                Likes: GetLong(item, "diggCount"),
                Comments: GetLong(item, "commentCount"),
                Shares: GetLong(item, "shareCount"),
                Saves: GetLong(item, "collectCount"),
                PublishedAtUtc: publishedAtUtc,
                SourceHandle: handle,
                SourceKey: SourceKey);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FirstLine(string text, string handle)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"Video de {handle}";
        }

        var line = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim() ?? text;

        const int maxLength = 140;
        return line.Length <= maxLength ? line : line[..maxLength].TrimEnd();
    }

    private static long? GetLong(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out var value)
        && value.TryGetInt64(out var number)
            ? number
            : null;

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
}
