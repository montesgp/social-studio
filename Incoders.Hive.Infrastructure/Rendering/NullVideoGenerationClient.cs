using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Infrastructure.Rendering;

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
