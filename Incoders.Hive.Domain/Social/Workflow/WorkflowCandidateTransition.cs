namespace Incoders.Hive.Domain.Social.Workflow;

/// <summary>
/// Encodes the allowed status transition table for a workflow candidate.
/// Pure static logic — no I/O, no EF references.
/// </summary>
public static class WorkflowCandidateTransition
{
    private static readonly HashSet<(WorkflowCandidateStatus From, WorkflowCandidateStatus To)> AllowedTransitions =
    [
        (WorkflowCandidateStatus.Discovered, WorkflowCandidateStatus.Queued),
        (WorkflowCandidateStatus.Queued, WorkflowCandidateStatus.Analyzing),
        (WorkflowCandidateStatus.Analyzing, WorkflowCandidateStatus.AnalysisFailed),
        (WorkflowCandidateStatus.Analyzing, WorkflowCandidateStatus.BatchQueued),
        (WorkflowCandidateStatus.BatchQueued, WorkflowCandidateStatus.BatchFailed),
        (WorkflowCandidateStatus.BatchQueued, WorkflowCandidateStatus.BatchStarted),
        (WorkflowCandidateStatus.BatchStarted, WorkflowCandidateStatus.Recovered),
    ];

    /// <summary>Returns <c>true</c> when transitioning from <paramref name="from"/> to
    /// <paramref name="to"/> is permitted by the domain model.</summary>
    public static bool CanMoveTo(WorkflowCandidateStatus from, WorkflowCandidateStatus to) =>
        AllowedTransitions.Contains((from, to));

    /// <summary>Returns <c>true</c> for terminal statuses (<see cref="WorkflowCandidateStatus.AnalysisFailed"/>,
    /// <see cref="WorkflowCandidateStatus.BatchFailed"/>, <see cref="WorkflowCandidateStatus.BatchStarted"/> and
    /// <see cref="WorkflowCandidateStatus.Recovered"/>).</summary>
    public static bool IsTerminal(WorkflowCandidateStatus status) =>
        status is WorkflowCandidateStatus.AnalysisFailed
            or WorkflowCandidateStatus.BatchFailed
            or WorkflowCandidateStatus.BatchStarted
            or WorkflowCandidateStatus.Recovered;
}
