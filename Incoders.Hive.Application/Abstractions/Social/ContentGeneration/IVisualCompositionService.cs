using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for producing a <see cref="VisualCompositionSpec"/> from generated content
/// and brand data. Separates composition reasoning from rendering mechanics.
/// </summary>
public interface IVisualCompositionService
{
    /// <summary>
    /// Composes a visual spec for the given format, using brand data (with defaults for missing fields).
    /// </summary>
    /// <param name="formatKey">The content format key (e.g. "carousel", "story").</param>
    /// <param name="generatedPayload">The format-specific generated content (deserialized JSON object or anonymous).</param>
    /// <param name="brand">The frozen brand snapshot for this job.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<VisualCompositionSpec> ComposeAsync(
        string formatKey,
        object generatedPayload,
        BrandSnapshot brand,
        CancellationToken ct);

    /// <summary>
    /// Composes one or more visual specs for the given format.
    /// Multi-asset formats (e.g. carousel) return N specs — one per slide/asset.
    /// Single-asset formats return a 1-element list.
    /// </summary>
    Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey,
        object generatedPayload,
        BrandSnapshot brand,
        CancellationToken ct);

    /// <summary>
    /// Returns the brand fields that were inferred (not explicitly supplied) during the last <see cref="ComposeAsync"/> call.
    /// </summary>
    IReadOnlyList<string> GetInferredFields();
}
