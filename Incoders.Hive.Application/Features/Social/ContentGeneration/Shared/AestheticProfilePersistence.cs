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
                .ToArray());

        return JsonSerializer.Serialize(dto, JsonOpts);
    }

    /// <summary>
    /// Deserializes the stored JSON into the enriched fields of a <see cref="ClientAestheticProfile"/>.
    /// Returns all-null parts when <paramref name="json"/> is null or malformed (graceful degrade).
    /// </summary>
    public static (IReadOnlyList<AestheticPaletteEntry>? Palette,
        IReadOnlyList<string>? StyleDescriptors,
        IReadOnlyList<string>? Motifs,
        string? CaptionVoice,
        IReadOnlyList<ProfilePostImageRef>? ReferenceImages) Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null, null, null, null);
        }

        try
        {
            var dto = JsonSerializer.Deserialize<StoredAestheticProfile>(json, JsonOpts);
            if (dto is null)
            {
                return (null, null, null, null, null);
            }

            var palette = (dto.Palette ?? [])
                .Select(p => new AestheticPaletteEntry(p.Hex, p.Role, p.Rank))
                .ToArray();
            var refs = (dto.ReferenceImages ?? [])
                .Select(r => new ProfilePostImageRef(r.SourcePostId, r.AssetIndex, r.StoragePath, r.ContentHash))
                .ToArray();

            return (palette, dto.StyleDescriptors ?? [], dto.Motifs ?? [], dto.CaptionVoice, refs);
        }
        catch
        {
            return (null, null, null, null, null);
        }
    }

    private sealed record StoredAestheticProfile(
        IReadOnlyList<StoredPaletteEntry>? Palette,
        IReadOnlyList<string>? StyleDescriptors,
        IReadOnlyList<string>? Motifs,
        string? CaptionVoice,
        IReadOnlyList<StoredImageRef>? ReferenceImages);

    private sealed record StoredPaletteEntry(string Hex, string? Role, int Rank);

    private sealed record StoredImageRef(string SourcePostId, int AssetIndex, string StoragePath, string ContentHash);
}
