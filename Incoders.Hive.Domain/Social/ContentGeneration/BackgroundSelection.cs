namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Discriminated result of tier-aware background selection (BG-03).
/// Closed hierarchy — callers must exhaust all cases.
/// Tier-1: ForcedDefault — no generation needed; the designated image is used as-is.
/// Tier-2: References  — manual template/reference images available for the format. The first
///                       applicable entry is used as the concrete background by the pipeline.
/// Tier-3: Creative    — no applicable manual template exists; the designer can generate freely.
/// </summary>
public abstract record BackgroundSelection
{
    private BackgroundSelection() { }

    /// <summary>
    /// Tier-1: a specific background image is forced for this format.
    /// The designer composes text/decoration ON this image; no background generation occurs.
    /// </summary>
    public sealed record ForcedDefault(string Url, string Name) : BackgroundSelection;

    /// <summary>
    /// Tier-2: one or more manual template/reference images are available. Product behavior uses
    /// the first applicable entry as the background, while still passing the set to the designer
    /// as creative guidance.
    /// </summary>
    public sealed record References(IReadOnlyList<BrandBackgroundTemplateSnapshot> Entries) : BackgroundSelection;

    /// <summary>
    /// Tier-3: no structured background guidance exists.
    /// The designer generates a brand-bound background freely.
    /// </summary>
    public sealed record Creative : BackgroundSelection;
}
