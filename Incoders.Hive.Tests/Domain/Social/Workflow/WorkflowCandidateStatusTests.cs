using Incoders.Hive.Domain.Social.Workflow;

namespace Incoders.Hive.Tests.Domain.Social.Workflow;

public sealed class WorkflowCandidateStatusTests
{
    [Theory]
    [InlineData(WorkflowCandidateStatus.Discovered, WorkflowCandidateStatus.Queued)]
    [InlineData(WorkflowCandidateStatus.Queued, WorkflowCandidateStatus.Analyzing)]
    [InlineData(WorkflowCandidateStatus.Analyzing, WorkflowCandidateStatus.AnalysisFailed)]
    [InlineData(WorkflowCandidateStatus.Analyzing, WorkflowCandidateStatus.BatchQueued)]
    [InlineData(WorkflowCandidateStatus.BatchQueued, WorkflowCandidateStatus.BatchFailed)]
    [InlineData(WorkflowCandidateStatus.BatchQueued, WorkflowCandidateStatus.BatchStarted)]
    public void CanMoveTo_ReturnsTrue_ForLegalTransitions(
        WorkflowCandidateStatus from,
        WorkflowCandidateStatus to)
    {
        Assert.True(WorkflowCandidateTransition.CanMoveTo(from, to));
    }

    [Theory]
    [InlineData(WorkflowCandidateStatus.Discovered, WorkflowCandidateStatus.Analyzing)]
    [InlineData(WorkflowCandidateStatus.Queued, WorkflowCandidateStatus.Discovered)]
    [InlineData(WorkflowCandidateStatus.Queued, WorkflowCandidateStatus.BatchQueued)]
    [InlineData(WorkflowCandidateStatus.Queued, WorkflowCandidateStatus.BatchStarted)]
    [InlineData(WorkflowCandidateStatus.AnalysisFailed, WorkflowCandidateStatus.Analyzing)]
    [InlineData(WorkflowCandidateStatus.BatchFailed, WorkflowCandidateStatus.BatchQueued)]
    [InlineData(WorkflowCandidateStatus.BatchStarted, WorkflowCandidateStatus.Analyzing)]
    [InlineData(WorkflowCandidateStatus.BatchStarted, WorkflowCandidateStatus.BatchQueued)]
    public void CanMoveTo_ReturnsFalse_ForIllegalTransitions(
        WorkflowCandidateStatus from,
        WorkflowCandidateStatus to)
    {
        Assert.False(WorkflowCandidateTransition.CanMoveTo(from, to));
    }

    [Fact]
    public void IsTerminal_OnlyForFailedAndBatchStarted()
    {
        Assert.True(WorkflowCandidateTransition.IsTerminal(WorkflowCandidateStatus.AnalysisFailed));
        Assert.True(WorkflowCandidateTransition.IsTerminal(WorkflowCandidateStatus.BatchFailed));
        Assert.True(WorkflowCandidateTransition.IsTerminal(WorkflowCandidateStatus.BatchStarted));
        Assert.False(WorkflowCandidateTransition.IsTerminal(WorkflowCandidateStatus.Discovered));
        Assert.False(WorkflowCandidateTransition.IsTerminal(WorkflowCandidateStatus.Queued));
        Assert.False(WorkflowCandidateTransition.IsTerminal(WorkflowCandidateStatus.Analyzing));
        Assert.False(WorkflowCandidateTransition.IsTerminal(WorkflowCandidateStatus.BatchQueued));
    }
}
