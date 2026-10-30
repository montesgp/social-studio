using SocialStudio.Domain.Social.Workflow;

namespace SocialStudio.Tests.Domain.Social.Workflow;

public sealed class WorkflowCandidateEnqueueKeyTests
{
    private static readonly Guid WorkflowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CandidateId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Create_IsStable_RegardlessOfFormatListOrder()
    {
        var ordered = new (string FormatKey, int Count)[]
        {
            ("reel-script", 2),
            ("caption", 1),
        };

        var reversed = new (string FormatKey, int Count)[]
        {
            ("caption", 1),
            ("reel-script", 2),
        };

        var keyA = WorkflowCandidateEnqueueKey.Create(WorkflowId, CandidateId, ordered);
        var keyB = WorkflowCandidateEnqueueKey.Create(WorkflowId, CandidateId, reversed);

        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void Create_Differs_WhenCompositionContentChanges()
    {
        var original = new (string FormatKey, int Count)[]
        {
            ("reel-script", 2),
        };

        var changedCount = new (string FormatKey, int Count)[]
        {
            ("reel-script", 3),
        };

        var keyA = WorkflowCandidateEnqueueKey.Create(WorkflowId, CandidateId, original);
        var keyB = WorkflowCandidateEnqueueKey.Create(WorkflowId, CandidateId, changedCount);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Create_Differs_WhenCandidateDiffers()
    {
        var composition = new (string FormatKey, int Count)[] { ("reel-script", 2) };
        var otherCandidate = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var keyA = WorkflowCandidateEnqueueKey.Create(WorkflowId, CandidateId, composition);
        var keyB = WorkflowCandidateEnqueueKey.Create(WorkflowId, otherCandidate, composition);

        Assert.NotEqual(keyA, keyB);
    }
}
