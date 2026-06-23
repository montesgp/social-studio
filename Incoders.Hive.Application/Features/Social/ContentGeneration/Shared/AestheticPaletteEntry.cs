namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// A single colour extracted from a client's recent posts, with an optional semantic role and a
/// prevalence rank. Phase 2 (CAG2-PA-03). Pure Application value object — no provider types.
/// </summary>
/// <remarks>
/// Palette is an OPTIONAL aesthetic signal. For many clients the colour palette is inconsequential
/// (their identity lives in their backgrounds/creatives, not in a marked palette), so an empty
/// palette is a VALID extraction result, not a degrade. When present, entries are role-ordered by
/// prevalence so the designer can weight Primary/Secondary/Accent/Neutral appropriately.
/// </remarks>
/// <param name="Hex">The colour as a hex string (e.g. "#1A2B3C").</param>
/// <param name="Role">Optional semantic role, e.g. "Primary", "Secondary", "Accent", "Neutral". Null when unclassified.</param>
/// <param name="Rank">Prevalence rank; 1 = most dominant.</param>
public sealed record AestheticPaletteEntry(
    string Hex,
    string? Role,
    int Rank);
