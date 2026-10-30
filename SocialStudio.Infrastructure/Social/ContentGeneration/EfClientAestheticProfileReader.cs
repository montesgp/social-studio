using System.Text.Json;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Infrastructure adapter for <see cref="IClientAestheticProfileReader"/>.
/// Reads the full aesthetic profile from the most recent completed <c>ProfileAiAnalysis</c>
/// for the given <c>(orgId, clientId)</c> pair.
///
/// <para>
/// Fields sourced:
/// <list type="bullet">
/// <item><c>DetectedBackgroundImages</c> from <c>ai_analysis_json</c> (PA-SEAM-01).</item>
/// <item><c>VoiceTone</c> from <c>ai_analysis_json</c>.</item>
/// <item><c>AverageSlidesPerCarousel</c> from <c>server_derived_stats_json</c> (CAG-PA-02).</item>
/// <item><c>AnalyzedAtUtc</c> from <c>CompletedAtUtc</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// No Apify call is made — this is a pure read over already-persisted analysis data
/// stored in <c>social.profile_analyses</c> (PA-SEAM-01, PA-SEAM-03).
/// </para>
/// Refs: design §3.2 (OQ-6); CAG-CG-05; CAG-PA-04, CAG-PA-05; tasks 5.8.
/// </summary>
public sealed class EfClientAestheticProfileReader : IClientAestheticProfileReader
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly AppIdentityDbContext _db;

    public EfClientAestheticProfileReader(AppIdentityDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ClientAestheticProfile?> GetProfileAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken ct)
    {
        // Select the most recently completed (Analyzed) analysis for this client.
        // In-progress, Failed, or other statuses are excluded (PA-SEAM-01d).
        var row = await _db.ProfileAnalyses
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == orgId &&
                x.ClientId == clientId &&
                x.Status == "Analyzed")
            .OrderByDescending(x => x.CompletedAtUtc)
            .Select(x => new
            {
                x.AiAnalysisJson,
                x.ServerDerivedStatsJson,
                x.AestheticProfileJson,
                x.CompletedAtUtc
            })
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        // An analysis can complete without an aesthetic (the extraction call failed or timed out, which
        // is non-fatal). Falling back to "no aesthetic" would make the next generation generic, so the
        // client's most recent real aesthetic is kept until a new extraction succeeds.
        var aestheticJson = row.AestheticProfileJson;
        if (string.IsNullOrWhiteSpace(aestheticJson))
        {
            aestheticJson = await _db.ProfileAnalyses
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == orgId &&
                    x.ClientId == clientId &&
                    x.Status == "Analyzed" &&
                    x.AestheticProfileJson != null)
                .OrderByDescending(x => x.CompletedAtUtc)
                .Select(x => x.AestheticProfileJson)
                .FirstOrDefaultAsync(ct);
        }

        var (descriptors, voiceTone) = DeserializeAiAnalysis(row.AiAnalysisJson);
        var avgSlides = DeserializeAverageSlides(row.ServerDerivedStatsJson);

        // Phase 2 (CAG2-PA-04): surface the enriched multimodal payload when present. Absent or
        // malformed JSON leaves the enriched fields null — Phase 1 behavior is preserved exactly.
        // client-aligned-carousel-generation: Read also returns BackgroundPattern, TypicalSlideCount,
        // and ColorPairings; absent keys (old blobs) → null, cold-start parity unchanged (CAC-BSP-04-a).
        // client-aesthetic-conceptual-detection (WU-2): Read now also returns ConceptualMethod and
        // ImageTypeRatio; absent keys (pre-v2 blobs) → null, cold-start parity unchanged (CACD-PA-05-b).
        // faithful-flat-slide-rendering (WU-4): Read now also returns BrandMark; absent keys
        // (pre-v3 blobs) → null, cold-start parity unchanged (FFS-PA-02-b).
        // aesthetic-freshness-versioning: Read also returns the stored SchemaVersion (PromptVersion).
        // Not part of ClientAestheticProfile — it is only reachable for freshness comparisons via
        // IProfileAnalysisRepository.GetLatestAestheticFreshnessAsync — so it is discarded here.
        // client-aesthetic-visual-sophistication (WU-3): Read now also returns DecorationMotifs;
        // absent keys (pre-v6 blobs) → null, cold-start parity unchanged (CAVS-PA-01-b).
        var (palette, styleDescriptors, motifs, captionVoice, referenceImages,
                backgroundPattern, typicalSlideCount, colorPairings,
                conceptualMethod, imageTypeRatio, brandMark, _, typography, backgroundTransparency,
                decorationMotifs) =
            AestheticProfilePersistence.Read(aestheticJson);

        return new ClientAestheticProfile(
            BackgroundDescriptors: descriptors,
            VoiceTone: voiceTone,
            AverageSlidesPerCarousel: avgSlides,
            AnalyzedAtUtc: row.CompletedAtUtc,
            Palette: palette,
            StyleDescriptors: styleDescriptors,
            Motifs: motifs,
            CaptionVoice: captionVoice,
            ReferenceImages: referenceImages,
            BackgroundPattern: backgroundPattern,
            TypicalSlideCount: typicalSlideCount,
            ColorPairings: colorPairings,
            ConceptualMethod: conceptualMethod,
            ImageTypeRatio: imageTypeRatio,
            BrandMark: brandMark,
            // exact-client-typography: observed per-role families/weights/size ratios drive
            // CompositionDefaults.ResolveTypography; null keeps the legacy 28/16 defaults.
            Typography: typography,
            // client-aesthetic-fidelity-fixes (WU-3a, D3): per-slide photo-transparency signal;
            // null keeps render-time treatment exactly as today (cold-start parity; WU-3b consumes it).
            BackgroundTransparency: backgroundTransparency,
            // client-aesthetic-visual-sophistication (WU-3): divider-rule/archetype/ornament
            // signal; null keeps render output byte-identical to today (cold-start parity).
            DecorationMotifs: decorationMotifs);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static (IReadOnlyList<string> Descriptors, string? VoiceTone) DeserializeAiAnalysis(
        string? json)
    {
        if (json is null)
        {
            return ([], null);
        }

        try
        {
            var dto = JsonSerializer.Deserialize<AiAnalysisProjection>(json, JsonOpts);
            return (dto?.DetectedBackgroundImages ?? [], dto?.VoiceTone);
        }
        catch
        {
            // Malformed JSON — degrade gracefully; callers fall back to Creative.
            return ([], null);
        }
    }

    private static double? DeserializeAverageSlides(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<StatsProjection>(json, JsonOpts);
            return dto?.AverageSlidesPerCarousel;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Minimal projection of the AI analysis JSON.
    /// Tolerant of missing keys (all properties nullable/optional).
    /// </summary>
    private sealed record AiAnalysisProjection(
        IReadOnlyList<string>? DetectedBackgroundImages = null,
        string? VoiceTone = null);

    /// <summary>
    /// Minimal projection of the server-derived stats JSON.
    /// Tolerant of missing keys.
    /// </summary>
    private sealed record StatsProjection(
        double? AverageSlidesPerCarousel = null);
}
