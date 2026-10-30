using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.SingleAnalyzer;

public sealed record SingleAnalyzerContentSnapshot(
    string PlatformKey,
    string CanonicalUrl,
    string? Title,
    string? Description,
    string? Author,
    string? ThumbnailUrl,
    int? DurationSeconds,
    IReadOnlyDictionary<string, long> Metrics,
    string? Transcript,
    IReadOnlyList<SingleAnalyzerMediaReference> MediaReferences,
    string? RawPayloadReference,
    IReadOnlyList<string> Tags,
    string? ContentType,
    DateTimeOffset? PublishedAtUtc)
{
    public static Result<SingleAnalyzerContentSnapshot> Create(
        string platformKey,
        string canonicalUrl,
        string? title,
        string? description,
        string? author,
        string? thumbnailUrl,
        int? durationSeconds,
        IReadOnlyDictionary<string, long> metrics,
        string? transcript,
        IReadOnlyList<SingleAnalyzerMediaReference> mediaReferences,
        string? rawPayloadReference,
        IReadOnlyList<string>? tags = null,
        string? contentType = null,
        DateTimeOffset? publishedAtUtc = null)
    {
        var safeMetrics = metrics ?? new Dictionary<string, long>();
        var safeMediaReferences = mediaReferences ?? Array.Empty<SingleAnalyzerMediaReference>();
        var safeTags = tags ?? Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(platformKey)
            || string.IsNullOrWhiteSpace(canonicalUrl)
            || durationSeconds < 0
            || safeMediaReferences.Any(x => !x.IsValid()))
        {
            return Result.Failure<SingleAnalyzerContentSnapshot>(SingleAnalyzerErrors.SnapshotInvalid);
        }

        return Result.Success(new SingleAnalyzerContentSnapshot(
            NormalizeKey(platformKey),
            canonicalUrl.Trim(),
            NormalizeOptional(title),
            NormalizeOptional(description),
            NormalizeOptional(author),
            NormalizeOptional(thumbnailUrl),
            durationSeconds,
            new Dictionary<string, long>(safeMetrics),
            NormalizeOptional(transcript),
            safeMediaReferences.ToArray(),
            NormalizeOptional(rawPayloadReference),
            safeTags.ToArray(),
            NormalizeOptional(contentType),
            publishedAtUtc));
    }

    private static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SingleAnalyzerMediaReference(
    string Kind,
    string Uri,
    string? MimeType)
{
    public bool IsValid() => !string.IsNullOrWhiteSpace(Kind) && !string.IsNullOrWhiteSpace(Uri);
}
