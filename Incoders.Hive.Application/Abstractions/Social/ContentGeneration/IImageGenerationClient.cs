using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for AI-based image generation (v1: not supported — NullImageGenerationClient).
/// Future adapters: Stable Diffusion, Imagen, DALL·E.
/// </summary>
public interface IImageGenerationClient
{
    Task<Result<RenderedAsset>> GenerateAsync(ImageGenerationRequest req, CancellationToken ct);
}

/// <summary>Request payload for image generation.</summary>
public sealed record ImageGenerationRequest(
    string Prompt,
    int Width,
    int Height,
    string? StyleHint);
