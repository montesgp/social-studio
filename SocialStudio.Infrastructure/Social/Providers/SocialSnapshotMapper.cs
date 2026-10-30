using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;

namespace SocialStudio.Infrastructure.Social.Providers;

internal static class SocialSnapshotMapper
{
    public static SocialContentSnapshot FromJson(
        string platformKey,
        string canonicalUrl,
        string payload,
        string rawPayloadReference)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        var root = document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0
            ? document.RootElement[0]
            : document.RootElement;

        return new SocialContentSnapshot(
            platformKey,
            GetString(root, "canonicalUrl") ?? GetString(root, "url") ?? canonicalUrl,
            GetString(root, "title") ?? GetString(root, "caption"),
            GetString(root, "description") ?? GetString(root, "text"),
            GetString(root, "author") ?? GetString(root, "username") ?? GetString(root, "ownerUsername"),
            GetString(root, "thumbnail") ?? GetString(root, "thumbnailUrl") ?? GetString(root, "displayUrl"),
            GetInt(root, "duration") ?? GetInt(root, "durationSeconds"),
            ReadMetrics(root),
            GetString(root, "transcript"),
            ReadMedia(root),
            rawPayloadReference,
            string.IsNullOrWhiteSpace(GetString(root, "transcript"))
                ? [new SocialContentWarning("transcript_missing", "social.warnings.transcript_missing")]
                : [],
            [],
            null,
            null);
    }

    private static string? GetString(JsonElement root, string property)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement root, string property)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(property, out var value)
            && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static IReadOnlyDictionary<string, long> ReadMetrics(JsonElement root)
    {
        var metrics = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object)
        {
            return metrics;
        }

        foreach (var property in new[] { "likes", "comments", "shares", "views", "playCount", "diggCount" })
        {
            if (root.TryGetProperty(property, out var value) && value.TryGetInt64(out var number))
            {
                metrics[property] = number;
            }
        }

        return metrics;
    }

    private static IReadOnlyList<SocialMediaReference> ReadMedia(JsonElement root)
    {
        var media = new List<SocialMediaReference>();
        var videoUrl = GetString(root, "videoUrl") ?? GetString(root, "video_url");
        if (!string.IsNullOrWhiteSpace(videoUrl))
        {
            media.Add(new SocialMediaReference("video", videoUrl, null));
        }

        var imageUrl = GetString(root, "imageUrl") ?? GetString(root, "displayUrl");
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            media.Add(new SocialMediaReference("image", imageUrl, null));
        }

        return media;
    }
}
