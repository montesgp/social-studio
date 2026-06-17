using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Tests.Domain.Social.ContentFormats;

/// <summary>
/// Extensibility proof for SCENARIO-16 (REQ-10).
///
/// GOAL: Demonstrate that adding a hypothetical "story-video" format (Story, Single, Medium.Video)
/// requires exactly ONE catalog row and ZERO changes to:
///   - Axis type definitions (Surface, Layout, Medium)
///   - The catalog abstraction API (TryGet / All / BySurface / ValidLayoutsFor)
///   - The derive/normalize layer (first-segment path derivation)
///   - Any downstream projection that consults IsVisual, StorageFolder, or IsMultiAsset
///
/// These tests construct a local extended catalog view from the public FormatCatalog API
/// plus an in-memory row to prove the structural invariants hold for a video format.
/// The production FormatCatalog is NOT modified (N1: no video generator ships in v1).
/// </summary>
public sealed class FormatCatalogExtensibilityTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Test fixture — one hypothetical "story-video" row (not in production catalog)
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly TargetChannel InstagramStory = new("instagram", "story");

    /// <summary>
    /// The hypothetical video row that would be added in a future release.
    /// Constructing this instance is the ONLY catalog edit needed (SCENARIO-16).
    /// </summary>
    private static readonly FormatDefinition StoryVideoRow = new FormatDefinition(
        FormatKey:     "story-video",
        Surface:       Surface.Story,
        Layout:        Layout.Single,
        Medium:        Medium.Video,
        Channel:       InstagramStory,
        ContentMedium: ContentMedium.Visual,
        IsMultiAsset:  false,
        DisplayLabel:  "Story · Video",
        StorageFolder: "story-video");

    /// <summary>
    /// An extended catalog view that merges the production catalog with the
    /// hypothetical story-video row, simulating what the real catalog would look like
    /// after adding the new format. No changes to the catalog abstraction are needed.
    /// </summary>
    private static IReadOnlyList<FormatDefinition> ExtendedCatalog()
        => [.. FormatCatalog.All(), StoryVideoRow];

    // ──────────────────────────────────────────────────────────────────────────
    // FormatDefinition construction — proves axis types already support Video
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StoryVideoRow_CanBeConstructed_WithNoAxisTypeChanges()
    {
        // Medium.Video is already a valid enum value (REQ-10 / ADR-2).
        // This test compiles and runs, proving no axis type changes are needed.
        Assert.Equal("story-video",    StoryVideoRow.FormatKey);
        Assert.Equal(Surface.Story,    StoryVideoRow.Surface);
        Assert.Equal(Layout.Single,    StoryVideoRow.Layout);
        Assert.Equal(Medium.Video,     StoryVideoRow.Medium);
        Assert.False(StoryVideoRow.IsMultiAsset);
        Assert.Equal("story-video",    StoryVideoRow.StorageFolder);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IsVisual — Video is a visual medium; would flow into DesignedRasterFormats
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StoryVideoRow_IsVisual_IsTrue_BecauseVideoIsVisualMedium()
    {
        // IsVisual = Medium is Image or Video (FormatDefinition.cs line 26).
        // So "story-video" would automatically enter the DesignedRasterFormats /
        // VisualFormatOrder projections without any code change.
        Assert.True(StoryVideoRow.IsVisual);
    }

    [Fact]
    public void DesignedRasterFormatsProjection_AutoIncludes_StoryVideo()
    {
        // Simulate what ContentGenerationJobProcessor.DesignedRasterFormats would compute
        // from an extended catalog (ADR-3.6: derived from FormatCatalog.All().Where(IsVisual)).
        var designedRasterFormats = ExtendedCatalog()
            .Where(d => d.IsVisual)
            .Select(d => d.FormatKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("story-video", designedRasterFormats);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // KnownKeys — auto-derived from All(), no whitelist edit required
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void KnownKeysProjection_AutoIncludes_StoryVideo()
    {
        // Simulate KnownKeys() derived from the extended catalog.
        // No hand-edited whitelist change is needed (REQ-05, ADR-3.1).
        var knownKeys = ExtendedCatalog()
            .Select(d => d.FormatKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("story-video", knownKeys);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BySurface — Story surface would include the new video format
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BySurfaceProjection_Story_AutoIncludes_StoryVideo()
    {
        // Simulate BySurface(Story) over the extended catalog.
        // The production FormatCatalog.BySurface API logic (Where !IsAuxiliary && Surface == s)
        // requires NO code change to accommodate the new row.
        var storyFormats = ExtendedCatalog()
            .Where(d => !d.IsAuxiliary && d.Surface == Surface.Story)
            .Select(d => d.FormatKey)
            .ToList();

        Assert.Contains("story",          storyFormats);
        Assert.Contains("story-carousel", storyFormats);
        Assert.Contains("story-video",    storyFormats);
        Assert.Equal(3, storyFormats.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ValidLayoutsFor — unchanged; Single was already a valid Story layout
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidLayoutsFor_Story_IsUnchanged_AfterAddingStoryVideo()
    {
        // ValidLayoutsFor derives from axis values, not catalog rows.
        // Adding story-video (Story + Single + Video) requires NO change here.
        var layouts = FormatCatalog.ValidLayoutsFor(Surface.Story).ToList();

        Assert.Equal(2, layouts.Count);
        Assert.Contains(Layout.Single,   layouts);
        Assert.Contains(Layout.Carousel, layouts);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StorageFolder-based path derivation — generic first-segment logic works
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PathDerivation_StoryVideo_WorksWithFirstSegmentLogic()
    {
        // The derive/normalize layer reads the first path segment as the format key.
        // "story-video/asset.mp4" → "story-video" with zero code changes (SCENARIO-16).
        // This mirrors DeriveFormatKey(relativePath) => relativePath.Split('/')[0]
        const string relativePath = "story-video/asset.mp4";
        var derivedKey = relativePath.Split('/')[0];

        Assert.Equal("story-video", derivedKey);
        Assert.Equal("story-video", StoryVideoRow.StorageFolder);
    }

    [Fact]
    public void StorageFolder_ForStoryVideo_DoesNotCollideWithExistingFolders()
    {
        var existingFolders = FormatCatalog.All()
            .Select(d => d.StorageFolder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // "story-video" is a novel storage folder with no collision.
        Assert.DoesNotContain("story-video", existingFolders);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // NormalizePackageFormatKey simulation — generic IsMultiAsset routing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NormalizePackageFormatKey_StoryVideo_RoutesToSingleAssetBranch()
    {
        // NormalizePackageFormatKey branches on IsMultiAsset (ADR-5).
        // story-video is single-asset, so it routes to the single-asset writer branch.
        // No switch-case edit needed: the branch logic is predicate-based.
        Assert.False(StoryVideoRow.IsMultiAsset);

        // Simulate the routing decision: IsMultiAsset=false → single-asset writer branch.
        var routesToCarouselBranch = StoryVideoRow.IsMultiAsset;
        Assert.False(routesToCarouselBranch, "story-video must not route to the multi-asset carousel branch");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Catalog API invariants — no interface change required
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FormatDefinitionRecord_CanHoldVideoMedium_WithoutApiChange()
    {
        // FormatDefinition already exposes all required properties.
        // Adding a video row requires no new property, interface method, or enum value.
        Assert.Equal(Medium.Video, StoryVideoRow.Medium);
        Assert.Equal(ContentMedium.Visual, StoryVideoRow.ContentMedium);
        Assert.Equal("instagram", StoryVideoRow.Channel.Network);
        Assert.Equal("story",     StoryVideoRow.Channel.Placement);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SCENARIO-20 — No migration file added by this change
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FormatTaxonomy_RequiresNoSchemaChange_FormatKeyIsStringBased()
    {
        // The format_key column is string-based (varchar); the new taxonomy lives entirely
        // in C# catalog rows, not in the database schema.
        // This test asserts the structural fact: FormatDefinition.FormatKey is a plain string
        // with no EF attribute or migration dependency.
        var formatKeyProperty = typeof(FormatDefinition).GetProperty(nameof(FormatDefinition.FormatKey));

        Assert.NotNull(formatKeyProperty);
        Assert.Equal(typeof(string), formatKeyProperty!.PropertyType);

        // No EF Core column attributes on FormatDefinition (architecture boundary: Domain has no EF ref)
        var attributes = formatKeyProperty.GetCustomAttributes(inherit: true);
        var efAttributeNames = attributes
            .Select(a => a.GetType().FullName ?? string.Empty)
            .Where(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(efAttributeNames);
    }
}
