namespace SocialStudio.Application.Features.Social.Workflow;

/// <summary>
/// Heuristic ETA knobs for the enqueue response. No real telemetry in v1 (explicit non-goal) — bound
/// from configuration so the estimate can move without a deploy, mirroring
/// <c>ContentGenerationBatchOptions</c>.
/// </summary>
public sealed class ContentWorkflowEtaOptions
{
    public const string SectionName = "ContentWorkflow:Eta";

    /// <summary>Minutes budgeted for the analysis stage of one candidate's sequence.</summary>
    public int AnalysisMinutes { get; set; } = 5;

    /// <summary>Minutes budgeted per generated piece across all enqueued candidates.</summary>
    public int PerPieceMinutes { get; set; } = 2;
}
