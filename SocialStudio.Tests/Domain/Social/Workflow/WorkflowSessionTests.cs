using SocialStudio.Domain.Social.Workflow;

namespace SocialStudio.Tests.Domain.Social.Workflow;

public sealed class WorkflowSessionTests
{
    [Fact]
    public void SetComposition_RejectsEmptyComposition()
    {
        var candidate = WorkflowCandidate.Create(Guid.NewGuid(), "external-1");

        var result = candidate.SetComposition([]);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowCandidateErrors.EmptyComposition, result.Error);
    }

    [Fact]
    public void SetComposition_Accepts_NonEmptyComposition()
    {
        var candidate = WorkflowCandidate.Create(Guid.NewGuid(), "external-1");

        var result = candidate.SetComposition([("reel-script", 1)]);

        Assert.True(result.IsSuccess);
        Assert.Single(candidate.Composition);
        Assert.Equal("reel-script", candidate.Composition[0].FormatKey);
    }

    [Fact]
    public void AddCandidate_RejectsDuplicateCandidateId()
    {
        var session = WorkflowSession.Create(Guid.NewGuid());
        var candidateId = Guid.NewGuid();

        var first = session.AddCandidate(WorkflowCandidate.Create(candidateId, "external-1"));
        var second = session.AddCandidate(WorkflowCandidate.Create(candidateId, "external-2"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal(WorkflowCandidateErrors.DuplicateCandidate, second.Error);
        Assert.Single(session.Candidates);
    }

    [Fact]
    public void AddCandidate_Accepts_DistinctCandidateIds()
    {
        var session = WorkflowSession.Create(Guid.NewGuid());

        var first = session.AddCandidate(WorkflowCandidate.Create(Guid.NewGuid(), "external-1"));
        var second = session.AddCandidate(WorkflowCandidate.Create(Guid.NewGuid(), "external-2"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, session.Candidates.Count);
    }
}
