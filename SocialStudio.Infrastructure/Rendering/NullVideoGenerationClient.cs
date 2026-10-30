using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Infrastructure.Rendering;

/// <summary>
/// Null adapter for <see cref="IVideoGenerationClient"/>.
/// v1: video generation is not supported. Returns a NotSupported failure.
/// Replace with a real adapter when a video-generation provider is integrated.
/// </summary>
public sealed class NullVideoGenerationClient : IVideoGenerationClient
{
    private static readonly Error NotSupportedError =
        Error.Failure("video-generation.not-supported-v1", "videoGeneration.notSupportedV1");

    public Task<Result<RenderedAsset>> GenerateAsync(VideoGenerationRequest req, CancellationToken ct) =>
        Task.FromResult(Result.Failure<RenderedAsset>(NotSupportedError));
}
