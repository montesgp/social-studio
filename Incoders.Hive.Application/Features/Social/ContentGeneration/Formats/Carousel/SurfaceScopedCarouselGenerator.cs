using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;

/// <summary>
/// Decorator that binds a shared <see cref="CarouselGenerator"/> to a specific surface-scoped
/// format key (e.g. "story-carousel" or "feed-carousel").
///
/// ADR-4: ONE CarouselGenerator handles all carousel surfaces. Two decorator instances are
/// registered in DI — one per catalog carousel row — so the registry resolves each key to
/// its own decorator while the underlying generation logic stays in one place.
///
/// PromptVersion is delegated to the inner generator so both surface keys always record
/// "carousel.v2" (ADR-7). GenerateBoxedAsync() delegates without modification — content is
/// surface-agnostic.
/// </summary>
internal sealed class SurfaceScopedCarouselGenerator : IFormatContentGenerator
{
    private readonly string _formatKey;
    private readonly CarouselGenerator _inner;

    public SurfaceScopedCarouselGenerator(string formatKey, CarouselGenerator inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatKey);
        ArgumentNullException.ThrowIfNull(inner);

        _formatKey = formatKey;
        _inner = inner;
    }

    /// <inheritdoc />
    public string FormatKey => _formatKey;

    /// <inheritdoc />
    /// <remarks>Delegates to the shared inner generator (ADR-7: both carousels use carousel.v2).</remarks>
    public string PromptVersion => _inner.PromptVersion;

    /// <inheritdoc />
    public Task<FormatGenerationResult<object>> GenerateBoxedAsync(
        GenerationContext ctx,
        CancellationToken cancellationToken) =>
        _inner.GenerateBoxedAsync(ctx, cancellationToken);
}
