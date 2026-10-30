namespace SocialStudio.Application.Features.Social.ContentGeneration.Batches;

/// <summary>
/// Platform-level knobs for batch generation. Bound from configuration so the ceiling can move
/// without a deploy of new code — and so it is never a literal buried in a handler.
/// </summary>
public sealed class ContentGenerationBatchOptions
{
    public const string SectionName = "ContentGeneration:Batches";

    /// <summary>
    /// Ceiling applied when a client sets no <c>MaxPiecesPerGeneration</c> of its own.
    ///
    /// This is a COST control, not a technical limit: every piece is a full AI + image run, so a
    /// twenty-piece batch costs twenty times a single one.
    /// </summary>
    public int DefaultMaxPiecesPerBatch { get; set; } = 20;
}
