using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

namespace Incoders.Hive.Infrastructure.Social.Providers.Apify;

/// <summary>
/// An error the ACTOR reported inside an otherwise successful response — most often "this hashtag does
/// not exist". Distinct from an unmappable item on purpose: the two demand opposite operator actions
/// (pick a different niche term vs. correct the item mapping).
/// </summary>
/// <param name="ErrorCode">The actor's machine code, e.g. <c>NOT_FOUND</c>. Absent → <c>null</c>.</param>
/// <param name="Message">The actor's human message. Absent → <c>null</c>.</param>
/// <param name="Input">The hashtag or query the actor rejected, so the log names the offending term.</param>
public sealed record NicheSearchProviderError(string? ErrorCode, string? Message, string? Input);

/// <summary>
/// Maps a TikTok niche-search Apify actor response into <see cref="DiscoveredContent"/>.
///
/// VERIFIED against a real run of the operator's configured TikTok Discover actor (2026-08-11), on top
/// of the previously documentation-derived mapping. That run showed the actor FLATTENS nested objects
/// into dot-notation property names (<c>"authorMeta.name"</c> is one property whose name contains a
/// dot, not an object), so every nested read goes through <see cref="GetNestedString"/>, which accepts
/// the nested and the flattened shape alike — other actors in the family still answer nested.
///
/// Every metric stays nullable and every absent field degrades to <c>null</c> rather than throwing or
/// being zero-filled, so a further actor shape only needs correcting in <see cref="MapItem"/> — one
/// obvious place — not the discovery chain, the scorer or the classifier.
/// </summary>
public sealed class TikTokNicheSearchAdapter
{
    private const string NetworkKey = "tiktok";
    private const string SourceKey = "niche";

    /// <summary>
    /// Keyword-search input shape, accepted by search-style TikTok scrapers. The resolver tries it
    /// SECOND, as the reshape retry after a hashtag-shaped body is rejected with HTTP 400 (design §7).
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

    /// <summary>
    /// Hashtag input shape, documented by the TikTok "Discover"/hashtag actors operators map most often
    /// (<c>{ "hashtags": ["software"], "shouldDownload*": false }</c>). The resolver tries this FIRST.
    ///
    /// The candidates come from <c>NicheHashtagDerivation</c> in the Application layer — a client's niche
    /// is free text ("Medical &amp; health"), never already a hashtag, and the derivation is identical
    /// for every network. The input is an ARRAY, so all candidates travel in ONE call; the adapter only
    /// dresses them in this actor's body and never re-derives them.
    /// </summary>
    public object BuildHashtagsInput(IReadOnlyList<string> hashtags, int resultsPerPage) => new
    {
        hashtags,
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
    /// (design §7.1). Returns the mapped candidates plus the total, skipped and provider-error counts so
    /// the caller can apply the "zero-of-many mapped → Failed / partial → Ran" rule without re-counting.
    ///
    /// Provider errors are NOT skipped items. The actor answers a hashtag that does not exist with an
    /// error ITEM inside a 200 response, and counting that as "unmappable" would report a correct actor
    /// and a correct mapping as a shape mismatch — sending the operator to fix what is not broken.
    /// </summary>
    public (
        IReadOnlyList<DiscoveredContent> Mapped,
        int TotalCount,
        int SkippedCount,
        IReadOnlyList<NicheSearchProviderError> ProviderErrors) MapItems(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array)
        {
            return ([], 0, 0, []);
        }

        var mapped = new List<DiscoveredContent>();
        var providerErrors = new List<NicheSearchProviderError>();
        var total = 0;

        foreach (var item in items.EnumerateArray())
        {
            total++;

            var providerError = ReadProviderError(item);
            if (providerError is not null)
            {
                providerErrors.Add(providerError);
                continue;
            }

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

        return (mapped, total, total - mapped.Count - providerErrors.Count, providerErrors);
    }

    /// <summary>
    /// Recognises the actor's error item — <c>{ "error": "…", "errorCode": "NOT_FOUND", "input": "…" }</c>
    /// — and returns <c>null</c> for anything else. Either field alone is enough: the family is not
    /// consistent about emitting both, and a real video item carries neither.
    /// </summary>
    private static NicheSearchProviderError? ReadProviderError(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var message = GetString(item, "error");
        var errorCode = GetString(item, "errorCode");

        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(errorCode))
        {
            return null;
        }

        return new NicheSearchProviderError(errorCode, message, GetString(item, "input"));
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

            // Video COVER only. The author avatar is deliberately not a fallback: it is the profile
            // picture, not the video still, and rendering it as a thumbnail would show the operator a
            // wall of faces that look like content stills. An absent thumbnail is honest.
            var thumbnailUrl = GetNestedString(item, "videoMeta", "coverUrl")
                ?? GetNestedString(item, "videoMeta", "originalCoverUrl")
                ?? GetNestedString(item, "covers", "default");

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

    /// <summary>
    /// Reads <c>parent.child</c> tolerating BOTH item shapes this actor family emits: a real nested
    /// object (<c>{ "authorMeta": { "name": "x" } }</c>) and a FLATTENED item where the dotted path is
    /// a single property name (<c>{ "authorMeta.name": "x" }</c>). Shape tolerance only — deliberately
    /// not a JSON path engine.
    /// </summary>
    private static string? GetNestedString(JsonElement root, string parent, string child)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty(parent, out var parentEl)
            && parentEl.ValueKind == JsonValueKind.Object)
        {
            var nested = GetString(parentEl, child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return GetString(root, $"{parent}.{child}");
    }
}
