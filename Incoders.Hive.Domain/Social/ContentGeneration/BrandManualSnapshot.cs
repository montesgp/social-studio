namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of a client's brand manual, captured at job-start time.
/// Empty collections mean the field was absent.
/// </summary>
public sealed record BrandManualSnapshot(
    IReadOnlyList<string> ColorPalette,
    IReadOnlyList<string> Logos,
    IReadOnlyList<string> Typography,
    string? VoiceAndTone,
    string? Notes);
