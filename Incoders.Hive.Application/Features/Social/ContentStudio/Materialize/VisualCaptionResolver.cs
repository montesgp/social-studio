using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;

/// <summary>
/// Resolves the caption for a visual format result: extract from the AI-generated output first, then
/// fall back to a deterministic derivation from the composition spec (Decision E3/Q2). Shared by every
/// materialization path (<see cref="MaterializeRunCommandHandler"/>, <see cref="MaterializeAnalysisCommandHandler"/>)
/// so the "caption is required, never empty" invariant cannot drift between them again — before this
/// type existed, the analysis-scoped path never extracted or fell back a caption at all.
/// </summary>
public static class VisualCaptionResolver
{
    public readonly record struct Resolution(Caption? Caption, bool UsedFallback);

    /// <summary>
    /// Resolves the caption for a single visual format result. Never throws and never leaves a
    /// caption null when ANY usable content exists in either the output JSON or the composition spec.
    /// </summary>
    public static Resolution Resolve(string? outputJson, string? compositionSpecJson)
    {
        var extracted = VisualPublicationCaption.Extract(outputJson);
        var captionResult = Caption.Create(extracted);

        if (captionResult.IsSuccess)
        {
            return new Resolution(captionResult.Value, UsedFallback: false);
        }

        var fallbackText = CaptionFallback.DeriveFromCompositionSpec(compositionSpecJson);
        var fallbackResult = Caption.Create(fallbackText);

        return new Resolution(fallbackResult.IsSuccess ? fallbackResult.Value : null, UsedFallback: true);
    }
}
