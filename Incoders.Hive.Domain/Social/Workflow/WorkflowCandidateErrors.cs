using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.Workflow;

public static class WorkflowCandidateErrors
{
    public static readonly Error EmptyComposition = Error.Validation(
        "social.workflow.candidate.empty_composition",
        "social.workflow.candidate.errors.empty_composition");

    public static readonly Error DuplicateCandidate = Error.Conflict(
        "social.workflow.candidate.duplicate",
        "social.workflow.candidate.errors.duplicate");

    public static readonly Error InvalidTransition = Error.Conflict(
        "social.workflow.candidate.invalid_transition",
        "social.workflow.candidate.errors.invalid_transition");

    /// <summary>No usable source URL — e.g. an <c>ai-ideas</c>-origin candidate with nothing to
    /// analyze. Rejected at enqueue rather than silently skipped.</summary>
    public static readonly Error NotAnalyzable = Error.Validation(
        "social.workflow.candidate.not_analyzable",
        "social.workflow.candidate.errors.not_analyzable");

    public static readonly Error NotFoundInSession = Error.NotFound(
        "social.workflow.candidate.not_found",
        "social.workflow.candidate.errors.not_found");

    public static Error UnknownFormat(string formatKey) => Error.Validation(
        "social.workflow.candidate.unknown_format",
        $"social.workflow.candidate.errors.unknown_format:{formatKey}");

    /// <summary>The bounded server-side wait for the candidate's analysis to reach <c>Analyzed</c>
    /// exceeded <c>ContentWorkflowEnqueueOptions.AnalysisWaitTimeout</c>.</summary>
    public static readonly Error AnalysisTimedOut = Error.Failure(
        "social.workflow.candidate.analysis_timed_out",
        "social.workflow.candidate.errors.analysis_timed_out");

    /// <summary>The candidate (or its session) was not found when the background sequencer picked up
    /// its queue item — defensive guard, should not happen in normal operation.</summary>
    public static readonly Error CandidateVanished = Error.NotFound(
        "social.workflow.candidate.vanished",
        "social.workflow.candidate.errors.vanished");
}
