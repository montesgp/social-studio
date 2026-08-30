using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
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
    /// <param name="aestheticProfile">
    /// Optional resolved <see cref="ClientAestheticProfile"/> for the ClientAesthetic policy. Null
    /// (default) = legacy behavior, unchanged for every non-ClientAesthetic client. Threaded into
    /// composers that implement a profile-aware capability so the client's real observed typography
    /// (family/weight) reaches the composed spec instead of the generic brand/default fallback.
    /// </param>
    Task<VisualCompositionSpec> ComposeAsync(
        string formatKey,
        object generatedPayload,
        BrandSnapshot brand,
        CancellationToken ct,
        ClientAestheticProfile? aestheticProfile = null);

    /// <summary>
    /// Composes one or more visual specs for the given format.
    /// Multi-asset formats (e.g. carousel) return N specs — one per slide/asset.
    /// Single-asset formats return a 1-element list.
    /// </summary>
    /// <param name="plan">
    /// Optional pre-computed background plan (WU-4 coherence refactor).
    /// When provided, carousel composers read <c>plan.ForSlide(i)</c> for each slide's
    /// background instead of calling <c>BackgroundSelector.Select</c> independently.
    /// Null = legacy fallback (backward-compatible; each composer decides its own selection).
    /// </param>
    /// <param name="aestheticProfile">
    /// Optional resolved <see cref="ClientAestheticProfile"/> for the ClientAesthetic policy. Null
    /// (default) = legacy behavior, unchanged for every non-ClientAesthetic client. Threaded into
    /// composers that implement a profile-aware capability (e.g. the carousel composer's real
    /// observed typography resolution) — see design refs CAC-CG-02, exact-client-typography.
    /// </param>
    Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey,
        object generatedPayload,
        BrandSnapshot brand,
        CancellationToken ct,
        CarouselBackgroundPlan? plan = null,
        ClientAestheticProfile? aestheticProfile = null);

    /// <summary>
    /// Returns the brand fields that were inferred (not explicitly supplied) during the last <see cref="ComposeAsync"/> call.
    /// </summary>
    IReadOnlyList<string> GetInferredFields();
}
