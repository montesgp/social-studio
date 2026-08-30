namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of the content strategy for a specific period, captured at job-start time.
/// Source is the ContentPlan.ContentStrategyText node.
/// </summary>
/// <param name="StrategyText">
/// The textual content strategy for the period. Null when the node has not been authored yet.
/// </param>
/// <param name="Source">
/// Provenance source as a short integer (e.g. 0 = None, 1 = Manual, 2 = AiGenerated).
/// Matches the ContentStrategyProvenanceSource column on the ContentPlan entity.
/// </param>
/// <param name="Status">
/// Provenance status as a short integer (e.g. 0 = NotSet, 1 = Draft, 2 = Approved).
/// Matches the ContentStrategyProvenanceStatus column on the ContentPlan entity.
/// </param>
public sealed record ContentStrategySnapshot(
    string? StrategyText,
    short Source,
    short Status);
