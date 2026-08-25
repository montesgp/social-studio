using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port that learns a client's visual aesthetic from their real posts using a multimodal
/// model. Phase 2 (CAG2-PA-03). Implementation lives in Infrastructure and calls the org's configured
/// multimodal AI route (the optional <c>extract-aesthetic</c> / <c>ai-aesthetic</c> workflow step).
/// </summary>
/// <remarks>
/// Palette is an OPTIONAL output: an empty palette is a VALID result (client without a marked
/// palette), never a failure. When no multimodal route is configured the caller skips this port
/// entirely and degrades to the Phase 1 text-descriptor signal.
/// </remarks>
public interface IClientAestheticStyleExtractor
{
    /// <summary>
    /// Extracts a multi-signal aesthetic from the client's durably-hosted post images paired with
    /// their parent post captions.
    /// </summary>
    /// <param name="organizationId">Owning organization (for AI route + credential resolution).</param>
    /// <param name="referenceImages">
    /// Ordered list of image↔caption pairs. Each pair carries a durable image reference and the
    /// caption of the parent post (null when the post has no caption or when the join found no
    /// matching post — image-only; never throws on missing captions). (CACD-PA-03; OQ-3.)
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<AestheticExtraction> ExtractAsync(
        Guid organizationId,
        IReadOnlyList<ProfilePostImageCaptionPair> referenceImages,
        CancellationToken ct);
}

/// <summary>
/// The multi-signal aesthetic produced by <see cref="IClientAestheticStyleExtractor"/>. Every signal
/// is optional; <see cref="Palette"/> may be empty when the client has no marked colour identity.
/// </summary>
public sealed record AestheticExtraction(
    IReadOnlyList<AestheticPaletteEntry> Palette,
    IReadOnlyList<string> StyleDescriptors,
    IReadOnlyList<string> Motifs,
    string? CaptionVoice,
    // client-aligned-carousel-generation (additive trailing nullable — existing construction sites unchanged):
    //
    // OQ-1: per-slide photo-vs-flat classification sequence detected by the multimodal extractor.
    // Null when the AI response contains no slideMixPattern or the pattern is ambiguous (CAC-BSP-01-b).
    IReadOnlyList<SlideBackgroundKind>? BackgroundPattern = null,
    // OQ-2: ranked rotating {BackgroundHex,TextHex} pairings derived via ColorPairingDerivation.Derive.
    // Null or empty when no joinable brand + palette roles exist (CAC-BSP-03-b).
    IReadOnlyList<ColorPairing>? ColorPairings = null,
    // client-aesthetic-conceptual-detection (additive trailing nullable — all existing construction sites
    // and `with` expressions compile unchanged; CACD-PA-04):
    //
    // OQ-1: structured conceptual art-direction method detected across the client's corpus.
    // Null when the model omits the field or returns an unrecognised method token (cold-start parity).
    ConceptualMethod? ConceptualMethod = null,
    // OQ-2: corpus-grounded image-type distribution (photo/flat/decorator proportions + sample size).
    // Null when the model omits imageTypeCounts or returns zero slides observed (cold-start parity).
    ImageTypeRatio? ImageTypeRatio = null,
    // faithful-flat-slide-rendering (additive trailing nullable — all existing construction sites and
    // `with` expressions compile unchanged; FFS-PA-01):
    //
    // Recurring brand mark detected across the client's corpus. Null when the model omits brandMark
    // or reports no recurring mark (cold-start parity; FFS-CG-03-a).
    BrandMarkDetection? BrandMark = null,
    // aesthetic-freshness-versioning (additive trailing nullable): the extractor's OWN schema/prompt
    // version, stamped onto the extraction it produces. Persistence stores THIS value (not a
    // hardcoded constant) so schema-version comparisons in `AestheticFreshness.Evaluate` reflect the
    // extractor that actually ran. Null on `Empty` / when the extractor didn't set it — Serialize
    // falls back to a safe default in that case.
    string? SchemaVersion = null,
    // measured-typical-slide-count (additive trailing nullable — CACD-PA-02 follow-up): the MEDIAN
    // number of slides the model actually counted across the attached posts, resisting outliers.
    // Null when the model omits/cannot determine it (cold-start parity) — the caller falls back to
    // BackgroundPattern's length, the prior (invented) proxy for "typical slide count".
    int? TypicalSlideCount = null,
    // exact-client-typography (additive TRAILING nullable): per-role families/weights/size ratios
    // OBSERVED in the client's posts. Null when the extractor could not read type off the pixels,
    // in which case the legacy single-family, hardcoded 28/16 px typography stands.
    IReadOnlyList<AestheticTypography>? Typography = null,
    // client-aesthetic-fidelity-fixes (WU-3a, D3, additive TRAILING nullable): per-slide
    // photo-transparency signal, index-aligned and parallel to <see cref="BackgroundPattern"/>.
    // A null ENTRY means that slide's signal is unmeasured; the whole list being null means the
    // signal was never measured at all (cold-start parity). Reconciled against BackgroundPattern's
    // length by <see cref="Shared.BackgroundTransparencyReconciler.Align"/> before it is stored
    // (CAFF-PA-03/04).
    IReadOnlyList<SlideBackgroundTransparency?>? BackgroundTransparency = null)
{
    /// <summary>An empty extraction (no signal) — used when extraction is skipped or yields nothing.</summary>
    public static AestheticExtraction Empty { get; } = new([], [], [], null);
}
