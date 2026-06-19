using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Domain.Social.ContentFormats;

/// <summary>
/// Immutable, catalog-derived registry of all supported content formats.
/// This is the SINGLE SOURCE OF TRUTH for the format taxonomy; every downstream
/// consumer (KnownFormatKeys, DesignedRasterFormats, VisualFormatOrder, etc.)
/// derives from this catalog rather than maintaining a separate literal list.
///
/// Design decisions (ADR-1, ADR-2, ADR-2b):
/// - TryGet is canonical-only: it returns null for legacy/alias keys such as "carousel".
/// - ResolveForAssembly is the ONLY alias-aware lookup and is used exclusively on
///   read/assemble paths; write/validation paths must NOT call it.
/// - KnownKeys() includes all canonical deliverable + auxiliary keys and excludes
///   the legacy "carousel" alias so API validation stays clean.
/// </summary>
public static class FormatCatalog
{
    // ──────────────────────────────────────────────────────────────────────────
    // Channel constants
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly TargetChannel InstagramReel  = new("instagram", "reel");
    private static readonly TargetChannel InstagramStory = new("instagram", "story");
    private static readonly TargetChannel InstagramFeed  = new("instagram", "feed");

    // ──────────────────────────────────────────────────────────────────────────
    // v1 deliverable rows (REQ-03 / design §"v1 catalog rows exact")
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<FormatDefinition> Deliverables =
    [
        new FormatDefinition(
            FormatKey:     "reel-script",
            Surface:       Surface.Script,
            Layout:        null,
            Medium:        Medium.Text,
            Channel:       InstagramReel,
            ContentMedium: ContentMedium.Text,
            IsMultiAsset:  false,
            DisplayLabel:  "Script",
            StorageFolder: "script"),

        new FormatDefinition(
            FormatKey:     "story-single",
            Surface:       Surface.Story,
            Layout:        Layout.Single,
            Medium:        Medium.Image,
            Channel:       InstagramStory,
            ContentMedium: ContentMedium.Visual,
            IsMultiAsset:  false,
            DisplayLabel:  "Story",
            StorageFolder: "story-single"),

        new FormatDefinition(
            FormatKey:     "story-carousel",
            Surface:       Surface.Story,
            Layout:        Layout.Carousel,
            Medium:        Medium.Image,
            Channel:       InstagramStory,
            ContentMedium: ContentMedium.Visual,
            IsMultiAsset:  true,
            DisplayLabel:  "Story · Carousel",
            StorageFolder: "story-carousel"),

        new FormatDefinition(
            FormatKey:     "feed-single",
            Surface:       Surface.Feed,
            Layout:        Layout.Single,
            Medium:        Medium.Image,
            Channel:       InstagramFeed,
            ContentMedium: ContentMedium.Visual,
            IsMultiAsset:  false,
            DisplayLabel:  "Feed Single",
            StorageFolder: "feed-single"),

        new FormatDefinition(
            FormatKey:     "feed-carousel",
            Surface:       Surface.Feed,
            Layout:        Layout.Carousel,
            Medium:        Medium.Image,
            Channel:       InstagramFeed,
            ContentMedium: ContentMedium.Visual,
            IsMultiAsset:  true,
            DisplayLabel:  "Feed · Carousel",
            StorageFolder: "feed-carousel"),
    ];

    // ──────────────────────────────────────────────────────────────────────────
    // Auxiliary rows (content-idea, cta-variation, caption, hook)
    // Included in KnownKeys() / registry; excluded from BySurface() and FE picker.
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<FormatDefinition> Auxiliaries =
    [
        MakeAuxiliary("content-idea"),
        MakeAuxiliary("cta-variation"),
        MakeAuxiliary("caption"),
        MakeAuxiliary("hook"),
    ];

    private static FormatDefinition MakeAuxiliary(string key) =>
        new FormatDefinition(
            FormatKey:     key,
            Surface:       Surface.Feed,   // Surface is unused for auxiliaries
            Layout:        null,
            Medium:        Medium.Text,
            Channel:       InstagramFeed,  // Channel is unused for auxiliaries
            ContentMedium: ContentMedium.Text,
            IsMultiAsset:  false,
            DisplayLabel:  key,
            StorageFolder: key)
        {
            IsAuxiliary = true,
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Combined list (deliverables + auxiliaries) — immutable
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<FormatDefinition> AllFormats =
        [.. Deliverables, .. Auxiliaries];

    // ──────────────────────────────────────────────────────────────────────────
    // Fast canonical lookup (FormatKey → definition); excludes legacy aliases
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, FormatDefinition> CanonicalIndex =
        AllFormats.ToDictionary(d => d.FormatKey, StringComparer.OrdinalIgnoreCase);

    // ──────────────────────────────────────────────────────────────────────────
    // Legacy alias map — ONLY used by ResolveForAssembly (read/assemble path)
    // Legacy single keys and "carousel" are NOT catalog rows; they map to read-only
    // assembly descriptors so historical rows/assets continue to export unchanged.
    // NEVER expose this map to validation/write paths.
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly FormatDefinition LegacyCarouselAssemblyDef =
        new FormatDefinition(
            FormatKey:     "carousel",
            Surface:       Surface.Feed,   // Surface unused for legacy assembly
            Layout:        Layout.Carousel,
            Medium:        Medium.Image,
            Channel:       InstagramFeed,  // Channel unused for legacy assembly
            ContentMedium: ContentMedium.Visual,
            IsMultiAsset:  true,
            DisplayLabel:  "Carousel (legacy)",
            StorageFolder: "carousel");

    private static readonly FormatDefinition LegacyStoryAssemblyDef =
        new FormatDefinition(
            FormatKey:     "story",
            Surface:       Surface.Story,
            Layout:        Layout.Single,
            Medium:        Medium.Image,
            Channel:       InstagramStory,
            ContentMedium: ContentMedium.Visual,
            IsMultiAsset:  false,
            DisplayLabel:  "Story (legacy)",
            StorageFolder: "story");

    private static readonly FormatDefinition LegacyFeedPostAssemblyDef =
        new FormatDefinition(
            FormatKey:     "feed-post",
            Surface:       Surface.Feed,
            Layout:        Layout.Single,
            Medium:        Medium.Image,
            Channel:       InstagramFeed,
            ContentMedium: ContentMedium.Visual,
            IsMultiAsset:  false,
            DisplayLabel:  "Feed Post (legacy)",
            StorageFolder: "feed-post");

    private static readonly IReadOnlyDictionary<string, FormatDefinition> AliasIndex =
        new Dictionary<string, FormatDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["story"] = LegacyStoryAssemblyDef,
            ["feed-post"] = LegacyFeedPostAssemblyDef,
            ["carousel"] = LegacyCarouselAssemblyDef,
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all catalog entries (5 v1 deliverables + 4 auxiliaries).
    /// </summary>
    public static IReadOnlyList<FormatDefinition> All() => AllFormats;

    /// <summary>
    /// Returns the definition for a CANONICAL format key, or null if the key is
    /// not a canonical catalog entry. This method has NO alias awareness:
    /// it returns null for "carousel", "feed-text", and any other
    /// legacy or invalid key (SCENARIO-03).
    /// </summary>
    public static FormatDefinition? TryGet(string formatKey) =>
        CanonicalIndex.TryGetValue(formatKey, out var def) ? def : null;

    /// <summary>
    /// Read-path/assemble-path lookup that resolves both canonical keys AND
    /// the legacy "carousel" alias. Use ONLY on read/assemble paths; write/validation
    /// paths must NOT call this — they use TryGet and the KnownKeys whitelist.
    /// Returns null for unknown keys that are neither canonical nor aliased.
    /// </summary>
    public static FormatDefinition? ResolveForAssembly(string storedKey)
    {
        if (CanonicalIndex.TryGetValue(storedKey, out var def))
            return def;

        return AliasIndex.TryGetValue(storedKey, out var alias) ? alias : null;
    }

    /// <summary>
    /// Returns all deliverable entries (non-auxiliary) for the given surface,
    /// ordered by catalog declaration sequence.
    /// Excludes auxiliary formats (content-idea, cta-variation, caption, hook).
    /// </summary>
    public static IReadOnlyList<FormatDefinition> BySurface(Surface surface) =>
        AllFormats
            .Where(d => !d.IsAuxiliary && d.Surface == surface)
            .ToList();

    /// <summary>
    /// Returns the layout options valid for the given surface.
    /// Script returns an empty list (no applicable layout axis).
    /// Story and Feed return [Single, Carousel].
    /// </summary>
    public static IReadOnlyList<Layout> ValidLayoutsFor(Surface surface) =>
        surface switch
        {
            Surface.Script => [],
            Surface.Story  => [Layout.Single, Layout.Carousel],
            Surface.Feed   => [Layout.Single, Layout.Carousel],
            _              => [],
        };

    /// <summary>
    /// Returns the canonical selectable/whitelist key set: all keys from All()
    /// (deliverables + auxiliaries). The legacy "carousel" alias is NOT included
    /// because it is rejected on write paths (CRITICAL-2, ADR-3.1).
    /// </summary>
    public static IReadOnlySet<string> KnownKeys() =>
        AllFormats
            .Select(d => d.FormatKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
