namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of a client's brand manual, captured at job-start time.
/// Empty collections mean the field was absent.
/// ColorPalette carries structured (Hex, Role, Rank) entries pre-sorted by prevalence.
/// </summary>
public sealed record BrandManualSnapshot(
    IReadOnlyList<WeightedColor> ColorPalette,
    IReadOnlyList<string> Logos,
    IReadOnlyList<BrandBackgroundTemplateSnapshot> BackgroundTemplates,
    IReadOnlyList<string> Typography,
    string? VoiceAndTone,
    string? Notes);
