namespace Incoders.Hive.Application.Features.Social.Workflow;

/// <summary>
/// Platform-level knobs for the enqueue background worker.
/// </summary>
public sealed class ContentWorkflowEnqueueOptions
{
    public const string SectionName = "ContentWorkflow:Enqueue";

    /// <summary>
    /// Maximum time the worker waits, server-side, for a candidate's <c>AnalyzeSingleContentCommand</c>
    /// to reach <c>Analyzed</c> before giving up and marking the candidate <c>AnalysisFailed</c>.
    /// Resolved open question from design: defaults to 10 minutes rather than being tied to the
    /// single-analyzer's own internal budget, since the two features can evolve independently.
    /// </summary>
    public TimeSpan AnalysisWaitTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Bounded concurrency for the hosted worker's <c>Parallel.ForEachAsync</c> fan-out.</summary>
    public int MaxConcurrency { get; set; } = 3;
}
