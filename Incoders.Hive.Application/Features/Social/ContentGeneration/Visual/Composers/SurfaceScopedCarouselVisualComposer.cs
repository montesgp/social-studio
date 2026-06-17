using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;

/// <summary>
/// Decorator that wraps a shared <see cref="CarouselVisualComposer"/> and overrides
/// <see cref="FormatKey"/> to the surface-scoped value from the <see cref="FormatDefinition"/>
/// (e.g. "story-carousel" or "feed-carousel").
///
/// ADR-4 / S5: one <see cref="CarouselVisualComposer"/> holds all composition logic; two
/// decorator registrations in DI map each catalog carousel key to this shared logic.
/// The decorator also threads the surface-scoped FormatKey into the emitted
/// <see cref="VisualCompositionSpec"/> so future per-surface template/background selection
/// resolves against the correct key rather than the inner's hardcoded "carousel".
/// In v1 both surfaces share the same templates so output is identical, but the plumbing
/// is correct for future divergence (S5 per-surface-template guard).
/// </summary>
internal sealed class SurfaceScopedCarouselVisualComposer : IFormatVisualComposer, IMultiAssetVisualComposer
{
    private readonly FormatDefinition _def;
    private readonly CarouselVisualComposer _inner;

    public SurfaceScopedCarouselVisualComposer(FormatDefinition def, CarouselVisualComposer inner)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(inner);

        _def = def;
        _inner = inner;
    }

    /// <inheritdoc />
    public string FormatKey => _def.FormatKey;

    /// <summary>
    /// Canvas dimensions for this surface. Story carousels are full-screen 9:16 (so the design fills
    /// the story frame); feed carousels keep the 4:5 feed ratio. The inner composer is surface-agnostic
    /// and emits the feed default, so the decorator re-stamps the correct dimensions per definition.
    /// </summary>
    private CompositionDimensions Dimensions => _def.Surface == Surface.Story
        ? CompositionDefaults.StoryDimensions
        : CompositionDefaults.CarouselDimensions;

    /// <inheritdoc />
    public VisualCompositionSpec Compose(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred)
    {
        var spec = _inner.Compose(generatedPayload, brand, inferred);
        return spec with { FormatKey = _def.FormatKey, Dimensions = Dimensions };
    }

    /// <inheritdoc />
    public IReadOnlyList<VisualCompositionSpec> ComposeMany(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred)
    {
        var specs = _inner.ComposeMany(generatedPayload, brand, inferred);
        return specs.Select(s => s with { FormatKey = _def.FormatKey, Dimensions = Dimensions }).ToList();
    }
}
