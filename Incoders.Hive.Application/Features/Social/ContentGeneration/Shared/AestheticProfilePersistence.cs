using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Serialization contract for the enriched aesthetic profile persisted in
/// <c>social.profile_analyses.aesthetic_profile_json</c>. Phase 2 (CAG2-PA-04). Shared by the writer
/// (profile analyzer enrichment, Application) and the reader (<c>EfClientAestheticProfileReader</c>,
/// Infrastructure) so the stored shape stays in one place. Palette is OPTIONAL — an empty palette is
/// a valid persisted state. Pure Application code: no provider/EF types.
/// </summary>
public static class AestheticProfilePersistence
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes an extraction + its durable reference images to the stored JSON shape.</summary>
    public static string Serialize(AestheticExtraction extraction, IReadOnlyList<ProfilePostImageRef> referenceImages)
    {
        var dto = new StoredAestheticProfile(
            Palette: extraction.Palette.Select(p => new StoredPaletteEntry(p.Hex, p.Role, p.Rank)).ToArray(),
            StyleDescriptors: extraction.StyleDescriptors.ToArray(),
            Motifs: extraction.Motifs.ToArray(),
            CaptionVoice: extraction.CaptionVoice,
            ReferenceImages: referenceImages
                .Select(r => new StoredImageRef(r.SourcePostId, r.AssetIndex, r.StoragePath, r.ContentHash))
                .ToArray(),
            // client-aligned-carousel-generation: additive keys; absent on old blobs → null on Read
            BackgroundPattern: extraction.BackgroundPattern?
                .Select(k => k.ToString())
                .ToArray(),
            TypicalSlideCount: extraction.BackgroundPattern?.Count,
            ColorPairings: extraction.ColorPairings?
                .Select(p => new StoredColorPairing(p.BackgroundHex, p.TextHex, p.Rank))
                .ToArray(),
            // client-aesthetic-conceptual-detection: additive keys; absent on old blobs → null on Read (OQ-4)
            ConceptualMethod: extraction.ConceptualMethod is null ? null
                : new StoredConceptualMethod(
                    Method: extraction.ConceptualMethod.Method.ToString(),
                    Rationale: extraction.ConceptualMethod.Rationale,
                    Cues: extraction.ConceptualMethod.Cues.ToArray()),
            ImageTypeRatio: extraction.ImageTypeRatio is null ? null
                : new StoredImageTypeRatio(
                    Photo: extraction.ImageTypeRatio.Photo,
                    Flat: extraction.ImageTypeRatio.FlatColor,
                    Decorator: extraction.ImageTypeRatio.Decorator,
                    SampleSize: extraction.ImageTypeRatio.SampleSize),
            // faithful-flat-slide-rendering: additive key; absent on pre-v3 blobs → null on Read (FFS-PA-02)
            BrandMark: extraction.BrandMark is null ? null
                : new StoredBrandMark(
                    Present: extraction.BrandMark.Present,
                    PositionHint: extraction.BrandMark.PositionHint,
                    ColorHex: extraction.BrandMark.ColorHex),
            // aesthetic-freshness-versioning: stamp the extraction's OWN schema version (the
            // extractor that actually produced it), NOT a hardcoded constant — otherwise every
            // stored profile would report the same version regardless of which extractor ran,
            // permanently breaking schema-version comparisons in AestheticFreshness.Evaluate.
            // Falls back to the pre-versioning default only when the extraction didn't set one
            // (e.g. AestheticExtraction.Empty).
            PromptVersion: extraction.SchemaVersion ?? "aesthetic-extractor/v2");

        return JsonSerializer.Serialize(dto, JsonOpts);
    }

    /// <summary>
    /// Deserializes the stored JSON into the enriched fields of a <see cref="ClientAestheticProfile"/>.
    /// Returns all-null parts when <paramref name="json"/> is null or malformed (graceful degrade).
    /// Absent keys (old blobs) deserialize to null — no exception (CAC-BSP-02-b cold-start;
    /// CACD-PA-05-b pre-v2 parity).
    /// </summary>
    public static (IReadOnlyList<AestheticPaletteEntry>? Palette,
        IReadOnlyList<string>? StyleDescriptors,
        IReadOnlyList<string>? Motifs,
        string? CaptionVoice,
        IReadOnlyList<ProfilePostImageRef>? ReferenceImages,
        IReadOnlyList<SlideBackgroundKind>? BackgroundPattern,
        int? TypicalSlideCount,
        IReadOnlyList<ColorPairing>? ColorPairings,
        ConceptualMethod? ConceptualMethod,
        ImageTypeRatio? ImageTypeRatio,
        BrandMarkDetection? BrandMark,
        // aesthetic-freshness-versioning (additive trailing element): the stored PromptVersion,
        // reachable so callers (freshness checks) can compare schema versions. Null on old blobs
        // that predate the PromptVersion key (malformed/absent → treated as oldest by
        // AestheticFreshness.ParseSchemaVersion).
        string? SchemaVersion) Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null, null, null, null, null, null, null, null, null, null, null);
        }

        try
        {
            var dto = JsonSerializer.Deserialize<StoredAestheticProfile>(json, JsonOpts);
            if (dto is null)
            {
                return (null, null, null, null, null, null, null, null, null, null, null, null);
            }

            var palette = (dto.Palette ?? [])
                .Select(p => new AestheticPaletteEntry(p.Hex, p.Role, p.Rank))
                .ToArray();
            var refs = (dto.ReferenceImages ?? [])
                .Select(r => new ProfilePostImageRef(r.SourcePostId, r.AssetIndex, r.StoragePath, r.ContentHash))
                .ToArray();

            // client-aligned-carousel-generation additive fields (absent on old blobs → null)
            var backgroundPattern = dto.BackgroundPattern is null
                ? null
                : (IReadOnlyList<SlideBackgroundKind>?)dto.BackgroundPattern
                    .Select(s => Enum.TryParse<SlideBackgroundKind>(s, ignoreCase: true, out var kind) ? kind : SlideBackgroundKind.FlatColor)
                    .ToArray();

            var colorPairings = dto.ColorPairings is null
                ? null
                : (IReadOnlyList<ColorPairing>?)dto.ColorPairings
                    .Select(p => new ColorPairing(p.BackgroundHex, p.TextHex, p.Rank))
                    .ToArray();

            // client-aesthetic-conceptual-detection additive fields (absent on old blobs → null; CACD-PA-05-b)
            ConceptualMethod? conceptualMethod = dto.ConceptualMethod is null ? null : new ConceptualMethod(
                Method: Enum.TryParse<ArtDirectionMethod>(dto.ConceptualMethod.Method, ignoreCase: true, out var artMethod)
                    ? artMethod : ArtDirectionMethod.Unknown,
                Rationale: dto.ConceptualMethod.Rationale ?? string.Empty,
                Cues: (IReadOnlyList<string>)(dto.ConceptualMethod.Cues ?? []));

            ImageTypeRatio? imageTypeRatio = dto.ImageTypeRatio is null ? null : new ImageTypeRatio(
                Photo: dto.ImageTypeRatio.Photo ?? 0.0,
                FlatColor: dto.ImageTypeRatio.Flat ?? 0.0,
                Decorator: dto.ImageTypeRatio.Decorator ?? 0.0,
                SampleSize: dto.ImageTypeRatio.SampleSize ?? 0);

            // faithful-flat-slide-rendering additive field (absent on pre-v3 blobs → null; FFS-PA-02-b)
            BrandMarkDetection? brandMark = dto.BrandMark is null ? null : new BrandMarkDetection(
                Present: dto.BrandMark.Present,
                PositionHint: dto.BrandMark.PositionHint,
                ColorHex: dto.BrandMark.ColorHex);

            return (palette, dto.StyleDescriptors ?? [], dto.Motifs ?? [], dto.CaptionVoice, refs,
                backgroundPattern, dto.TypicalSlideCount, colorPairings,
                conceptualMethod, imageTypeRatio, brandMark, dto.PromptVersion);
        }
        catch
        {
            return (null, null, null, null, null, null, null, null, null, null, null, null);
        }
    }

    /// <summary>
    /// client-analysis-data admin panel: patches ONLY the <c>BackgroundPattern</c> (and its
    /// derived <c>TypicalSlideCount</c>) on an existing stored aesthetic profile, round-tripping
    /// every other field unchanged. Used by the admin edit flow so a manual correction never
    /// corrupts palette/motifs/reference-images/etc. captured by the extractor.
    /// <para>
    /// When <paramref name="existingJson"/> is null, empty, or malformed, starts from an empty
    /// payload (all other fields null) rather than throwing — same graceful-degrade contract as
    /// <see cref="Read"/>.
    /// </para>
    /// </summary>
    public static string WithBackgroundPattern(
        string? existingJson,
        IReadOnlyList<SlideBackgroundKind> backgroundPattern)
    {
        StoredAestheticProfile dto;
        try
        {
            dto = string.IsNullOrWhiteSpace(existingJson)
                ? EmptyStoredProfile
                : JsonSerializer.Deserialize<StoredAestheticProfile>(existingJson, JsonOpts) ?? EmptyStoredProfile;
        }
        catch
        {
            dto = EmptyStoredProfile;
        }

        var patched = dto with
        {
            BackgroundPattern = backgroundPattern.Select(k => k.ToString()).ToArray(),
            TypicalSlideCount = backgroundPattern.Count,
        };

        return JsonSerializer.Serialize(patched, JsonOpts);
    }

    private static readonly StoredAestheticProfile EmptyStoredProfile = new(
        Palette: null,
        StyleDescriptors: null,
        Motifs: null,
        CaptionVoice: null,
        ReferenceImages: null);

    private sealed record StoredAestheticProfile(
        IReadOnlyList<StoredPaletteEntry>? Palette,
        IReadOnlyList<string>? StyleDescriptors,
        IReadOnlyList<string>? Motifs,
        string? CaptionVoice,
        IReadOnlyList<StoredImageRef>? ReferenceImages,
        // client-aligned-carousel-generation additive keys (nullable; absent on old blobs)
        IReadOnlyList<string>? BackgroundPattern = null,
        int? TypicalSlideCount = null,
        IReadOnlyList<StoredColorPairing>? ColorPairings = null,
        // client-aesthetic-conceptual-detection additive keys (nullable; absent on old blobs; OQ-4)
        StoredConceptualMethod? ConceptualMethod = null,
        StoredImageTypeRatio? ImageTypeRatio = null,
        string? PromptVersion = null,
        // faithful-flat-slide-rendering additive key (nullable; absent on pre-v3 blobs; FFS-PA-02)
        StoredBrandMark? BrandMark = null);

    private sealed record StoredPaletteEntry(string Hex, string? Role, int Rank);

    private sealed record StoredImageRef(string SourcePostId, int AssetIndex, string StoragePath, string ContentHash);

    private sealed record StoredColorPairing(string BackgroundHex, string TextHex, int Rank);

    /// <summary>Stored shape for ConceptualMethod; method serialized as its enum name string.</summary>
    private sealed record StoredConceptualMethod(
        string? Method = null,
        string? Rationale = null,
        IReadOnlyList<string>? Cues = null);

    /// <summary>
    /// Stored shape for ImageTypeRatio. Field <c>Flat</c> maps to <c>FlatColor</c> on the
    /// Application VO; <c>photo + flat + decorator = sampleSize</c> (proportions sum ~1.0).
    /// </summary>
    private sealed record StoredImageTypeRatio(
        double? Photo = null,
        double? Flat = null,
        double? Decorator = null,
        int? SampleSize = null);

    /// <summary>Stored shape for BrandMarkDetection — a straight field-for-field mirror (FFS-PA-02).</summary>
    private sealed record StoredBrandMark(
        bool Present = false,
        string? PositionHint = null,
        string? ColorHex = null);
}
