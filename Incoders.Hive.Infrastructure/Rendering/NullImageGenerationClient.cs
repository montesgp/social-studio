using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Infrastructure.Rendering;

/// <summary>
/// Null adapter for <see cref="IImageGenerationClient"/>.
/// v1: image generation is not supported. Returns a NotSupported failure.
/// Replace with a real adapter when an image-generation provider is integrated.
/// </summary>
public sealed class NullImageGenerationClient : IImageGenerationClient
{
    private static readonly Error NotSupportedError =
        Error.Failure("image-generation.not-supported-v1", "imageGeneration.notSupportedV1");

    public Task<Result<RenderedAsset>> GenerateAsync(ImageGenerationRequest req, CancellationToken ct) =>
        Task.FromResult(Result.Failure<RenderedAsset>(NotSupportedError));
}
