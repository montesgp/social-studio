using Incoders.Hive.Domain.Social.Workflow;

namespace Incoders.Hive.Tests.Domain.Social.Workflow;

public sealed class WorkflowEtaEstimatorTests
{
    [Fact]
    public void Estimate_AddsAnalysisMinutes_PlusPerPieceTimesTotalPieces()
    {
        var minutes = WorkflowEtaEstimator.Estimate(analysisMinutes: 5, perPieceMinutes: 2, totalPieces: 4);

        Assert.Equal(13, minutes);
    }

    [Fact]
    public void Estimate_ReturnsAnalysisMinutesOnly_WhenTotalPiecesIsZero()
    {
        var minutes = WorkflowEtaEstimator.Estimate(analysisMinutes: 5, perPieceMinutes: 2, totalPieces: 0);

        Assert.Equal(5, minutes);
    }
}
