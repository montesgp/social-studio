using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;

/// <summary>
/// Optional capability for format composers that produce multiple assets per job.
/// Carousel implements this to fan out N slide specs.
/// Single-asset composers may also implement this and return a 1-element list.
/// The pipeline checks for this interface and iterates; if absent it falls back to
/// wrapping the single <see cref="IFormatVisualComposer.Compose"/> result in a list.
/// </summary>
public interface IMultiAssetVisualComposer
{
    /// <summary>
    /// Returns an ordered list of composition specs — one per asset to render.
    /// For carousel: one per slide (N ≥ 1, or empty if payload has no slides).
    /// For single-asset formats: a 1-element list wrapping <c>Compose</c>.
    /// </summary>
    IReadOnlyList<VisualCompositionSpec> ComposeMany(
        object generatedPayload,
        BrandSnapshot brand,
        InferenceAccumulator inferred);
}
