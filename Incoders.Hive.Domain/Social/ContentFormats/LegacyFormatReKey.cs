namespace Incoders.Hive.Domain.Social.ContentFormats;

/// <summary>
/// Pure mapping from the pre-taxonomy legacy format keys to the canonical
/// catalog keys, used by the one-time package reconstruction migration.
///
/// Mapping (see <see cref="FormatCatalog"/> for the canonical rows):
/// <list type="bullet">
///   <item><c>story</c>     → <c>story-single</c></item>
///   <item><c>flyer</c>     → <c>story-single</c> (flyer was previously mutated to story)</item>
///   <item><c>feed-post</c> → <c>feed-single</c></item>
///   <item><c>carousel</c>  → <c>feed-carousel</c></item>
/// </list>
///
/// This type is intentionally free of storage/DB concerns so it can be unit
/// tested in isolation; the migration orchestrator applies these results.
/// </summary>
public static class LegacyFormatReKey
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["story"]     = "story-single",
            ["flyer"]     = "story-single",
            ["feed-post"] = "feed-single",
            ["carousel"]  = "feed-carousel",
        };

    /// <summary>
    /// The set of legacy keys this migration knows how to re-key. Used by the
    /// orchestrator to query only the rows that need work.
    /// </summary>
    public static IReadOnlySet<string> LegacyKeys { get; } =
        Map.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a legacy format key to its canonical replacement.
    /// Returns false for keys that are already canonical or otherwise unknown,
    /// making callers idempotent.
    /// </summary>
    public static bool TryReKeyFormat(string formatKey, out string canonicalKey)
    {
        if (!string.IsNullOrWhiteSpace(formatKey) && Map.TryGetValue(formatKey, out var mapped))
        {
            canonicalKey = mapped;
            return true;
        }

        canonicalKey = formatKey;
        return false;
    }

    /// <summary>
    /// Re-keys a relative asset path to the canonical layout, preserving the
    /// file extension and (for multi-asset formats) the slide file name.
    /// <list type="bullet">
    ///   <item><c>("story.png", "story-single")</c>            → <c>story-single.png</c></item>
    ///   <item><c>("flyer.png", "story-single")</c>            → <c>story-single.png</c></item>
    ///   <item><c>("carousel/slide_2.png", "feed-carousel")</c> → <c>feed-carousel/slide_2.png</c></item>
    /// </list>
    /// </summary>
    public static string ReKeyRelativePath(string legacyRelativePath, string canonicalKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyRelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalKey);

        var trimmed = legacyRelativePath.Trim('/');
        var slashIndex = trimmed.IndexOf('/');

        // Multi-asset (folder) form: replace the leading folder segment only.
        if (slashIndex >= 0)
        {
            var rest = trimmed[(slashIndex + 1)..];
            return $"{canonicalKey}/{rest}";
        }

        // Single-asset form: rename "<legacy>.ext" → "<canonical>.ext".
        var dotIndex = trimmed.LastIndexOf('.');
        var extension = dotIndex >= 0 ? trimmed[dotIndex..] : string.Empty;
        return $"{canonicalKey}{extension}";
    }
}
