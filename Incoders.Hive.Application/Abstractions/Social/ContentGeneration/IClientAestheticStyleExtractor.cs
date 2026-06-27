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
    /// Extracts a multi-signal aesthetic from the client's durably-hosted post images + captions.
    /// </summary>
    /// <param name="organizationId">Owning organization (for AI route + credential resolution).</param>
    /// <param name="referenceImages">Durable references to the client's recent post images.</param>
    /// <param name="captions">Recent post captions (best-effort; may be empty).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AestheticExtraction> ExtractAsync(
        Guid organizationId,
        IReadOnlyList<ProfilePostImageRef> referenceImages,
        IReadOnlyList<string> captions,
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
    IReadOnlyList<ColorPairing>? ColorPairings = null)
{
    /// <summary>An empty extraction (no signal) — used when extraction is skipped or yields nothing.</summary>
    public static AestheticExtraction Empty { get; } = new([], [], [], null);
}
