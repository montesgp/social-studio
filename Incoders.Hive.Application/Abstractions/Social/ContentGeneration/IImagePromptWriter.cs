using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

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
    ClientAestheticProfile Aesthetic);
