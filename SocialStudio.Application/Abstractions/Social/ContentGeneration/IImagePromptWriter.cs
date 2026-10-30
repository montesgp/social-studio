using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for writing a per-slide image prompt from a client's aesthetic profile.
/// Implemented over the org's TEXT route via <see cref="Ai.IAiCompletionGateway"/> (OQ-6).
/// On weak/empty aesthetic signal OR a failed/empty write → returns <see cref="Result"/> failure →
/// the caller degrades that slide to the CSS designer path (resilience contract).
/// No provider type may appear in this interface or its request record.
/// Refs: IMG-03; design §3.2, OQ-6.
/// </summary>
public interface IImagePromptWriter
{
    Task<Result<string>> WriteAsync(
        ImagePromptRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Input to the image-prompt writer for a single creative slide.
/// Carries only Guid, int, string, and Application VO types — no provider SDK types.
/// </summary>
public sealed record ImagePromptRequest(
    Guid OrganizationId,
    Guid? ClientId,
    int SlideIndex,
    /// <summary>The slide's generated copy; drives on-topic image prompting.</summary>
    string SlideCopySummary,
    /// <summary>
    /// The client's aesthetic profile — style descriptors, motifs, palette, voice, and
    /// reference context used to produce a brand-accurate image prompt.
    /// </summary>
    ClientAestheticProfile Aesthetic,
    // client-aesthetic-conceptual-detection (additive trailing nullable — all existing construction
    // sites compile unchanged):
    //
    // OQ-5: the upstream CreativeSlideIndexResolver resolves which slides are creative (Photo) and
    // which are flat; the writer reads the resolved kind here so it can emit ratio-aware cues
    // without reassigning photo-vs-flat (single-responsibility; CACD-CG-02-a/b/c).
    // Null → SlideKind unspecified; ratio cue is omitted (safe cold-start default).
    SlideBackgroundKind? SlideKind = null,
    // client-style-fidelity (CSF-CG-08, additive trailing nullable): how the client treats the image
    // at THIS slide position in their own carousels (e.g. "product cut-out on seamless black
    // backdrop"), and the backdrop tone observed behind their text. Null → no treatment cue.
    string? SlideTreatment = null,
    string? BackdropHex = null);
