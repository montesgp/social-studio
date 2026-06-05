namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Discriminated result of tier-aware background selection (BG-03).
/// Closed hierarchy — callers must exhaust all cases.
/// Tier-1: ForcedDefault — no generation needed; the designated image is used as-is.
/// Tier-2: References  — reference images passed as multimodal style guides to the designer.
/// Tier-3: Creative    — the designer generates a brand-bound background freely.
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
    /// Tier-2: one or more reference images are available as visual style guides.
    /// Passed to the designer as multimodal inputs.
    /// </summary>
    public sealed record References(IReadOnlyList<BrandBackgroundTemplateSnapshot> Entries) : BackgroundSelection;

    /// <summary>
    /// Tier-3: no structured background guidance exists.
    /// The designer generates a brand-bound background freely.
    /// </summary>
    public sealed record Creative : BackgroundSelection;
}
