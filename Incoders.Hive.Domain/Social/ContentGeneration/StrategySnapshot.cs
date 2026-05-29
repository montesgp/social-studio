namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of a client's content strategy, captured at job-start time.
/// </summary>
public sealed record StrategySnapshot(
    IReadOnlyList<string> ContentPillars,
    IReadOnlyList<string> ToneKeywords,
    string? TargetAudience,
    string? PostingFrequency);
