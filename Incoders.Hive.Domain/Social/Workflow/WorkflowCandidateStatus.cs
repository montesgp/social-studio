namespace Incoders.Hive.Domain.Social.Workflow;

/// <summary>
/// Lifecycle status of a single workflow candidate's analysis-then-batch sequence.
/// </summary>
/// <remarks>
/// Valid transitions:
///   Discovered → Queued → Analyzing → AnalysisFailed (terminal) | BatchQueued
///   BatchQueued → BatchFailed (terminal) | BatchStarted (terminal)
///
/// <see cref="Discovered"/> is the pre-enqueue state: the row exists (discovery persisted it so the
/// operator can select/compose it) but the operator has not composed and submitted it yet. It is
/// intentionally distinct from <see cref="Queued"/>, which now means "genuinely submitted to the
/// analysis-then-batch pipeline, awaiting worker pickup" — the two were previously conflated, which made
/// the workflow history read surface show every discovered-but-untouched candidate as "Queued".
/// <see cref="Discovered"/> candidates are excluded from that read surface; only <see cref="Queued"/>
/// and later statuses are.
/// </remarks>
public enum WorkflowCandidateStatus
{
    Discovered = 0,
    Queued = 1,
    Analyzing = 2,
    AnalysisFailed = 3,
    BatchQueued = 4,
    BatchFailed = 5,
    BatchStarted = 6,
}
