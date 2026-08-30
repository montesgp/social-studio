using System.Text.Json;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;

/// <summary>
/// Extracts the ready-to-publish post caption (description + hashtags) from a visual format's
/// generated <c>OutputJson</c>.
///
/// <para>
/// The visual generators (carousel/story/feed-post) emit <c>publicationDescription</c> and
/// <c>hashtags</c> in the SAME AI call as the design content, so a materialized studio post carries
/// its caption without a separate caption-generation call. The payload is serialized with
/// <see cref="JsonSerializerDefaults.Web"/> (camelCase), so field names match the JSON verbatim.
/// </para>
///
/// <para>
/// Best-effort and defensive: returns <c>null</c> when the JSON is absent, malformed, not an object,
/// or carries neither field — never throws.
/// </para>
/// </summary>
public static class VisualPublicationCaption
{
    public static string? Extract(string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(outputJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var description = root.TryGetProperty("publicationDescription", out var d)
                && d.ValueKind == JsonValueKind.String
                ? d.GetString()?.Trim()
                : null;

            var hashtags = new List<string>();
            if (root.TryGetProperty("hashtags", out var h) && h.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in h.EnumerateArray())
                {
                    if (tag.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var value = tag.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        hashtags.Add(value);
                    }
                }
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(description))
            {
                parts.Add(description);
            }

            if (hashtags.Count > 0)
            {
                parts.Add(string.Join(" ", hashtags));
            }

            return parts.Count > 0 ? string.Join("\n\n", parts) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
