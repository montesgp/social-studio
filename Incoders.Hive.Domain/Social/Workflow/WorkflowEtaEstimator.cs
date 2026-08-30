namespace Incoders.Hive.Domain.Social.Workflow;

/// <summary>
/// Heuristic estimate only — no real ETA telemetry in v1 (explicit non-goal). Surfaced to the
/// operator as "approx." with a link to the content-generation-jobs screen.
/// </summary>
public static class WorkflowEtaEstimator
{
    public static int Estimate(int analysisMinutes, int perPieceMinutes, int totalPieces) =>
        analysisMinutes + (perPieceMinutes * totalPieces);
}
