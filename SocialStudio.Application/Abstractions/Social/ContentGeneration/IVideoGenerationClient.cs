using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for AI-based video generation (v1: not supported — NullVideoGenerationClient).
/// Future adapters: Runway, Sora, Pika.
/// </summary>
public interface IVideoGenerationClient
{
    Task<Result<RenderedAsset>> GenerateAsync(VideoGenerationRequest req, CancellationToken ct);
}

/// <summary>Request payload for video generation.</summary>
public sealed record VideoGenerationRequest(
    string Prompt,
    int DurationSeconds,
    string? StyleHint);
