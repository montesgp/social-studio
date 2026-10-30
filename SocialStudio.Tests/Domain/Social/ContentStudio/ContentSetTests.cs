using SocialStudio.Domain.Social.ContentStudio;

namespace SocialStudio.Tests.Domain.Social.ContentStudio;

public sealed class ContentSetTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateForRun_IsGenerationRun_AndEmpty()
    {
        var origin = SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, Guid.NewGuid());

        var set = ContentSet.CreateForRun(ContentSetId.New(), Org, Client, origin, Now).Value;

        Assert.Equal(SetKind.GenerationRun, set.Kind);
        Assert.Empty(set.Members);
    }

    [Fact]
    public void CreateForRun_WithoutRunId_Fails()
    {
        var origin = new SetOrigin(GeneratorKind.SingleAnalyzer, null, null, null);

        var result = ContentSet.CreateForRun(ContentSetId.New(), Org, Client, origin, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void AddMember_PreservesOrder()
    {
        var set = NewSet();
        var a = ContentItemId.New();
        var b = ContentItemId.New();

        Assert.True(set.AddMember(a, 0).IsSuccess);
        Assert.True(set.AddMember(b, 1).IsSuccess);

        Assert.Equal(2, set.Members.Count);
        Assert.Equal(a, set.Members[0].ItemId);
        Assert.Equal(b, set.Members[1].ItemId);
    }

    [Fact]
    public void AddMember_Duplicate_IsRejected()
    {
        var set = NewSet();
        var a = ContentItemId.New();
        set.AddMember(a, 0);

        var result = set.AddMember(a, 1);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.MemberAlreadyExists, result.Error);
    }

    [Fact]
    public void RemoveMember_RemovesIt_LeavingTheRest()
    {
        var set = NewSet();
        var a = ContentItemId.New();
        var b = ContentItemId.New();
        set.AddMember(a, 0);
        set.AddMember(b, 1);

        Assert.True(set.RemoveMember(a).IsSuccess);

        Assert.Single(set.Members);
        Assert.Equal(b, set.Members[0].ItemId);
    }

    [Fact]
    public void RemoveMember_NotPresent_Fails()
    {
        var set = NewSet();

        var result = set.RemoveMember(ContentItemId.New());

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.MemberNotFound, result.Error);
    }

    [Fact]
    public void ReviewSummary_From_CountsByState()
    {
        var summary = ReviewSummary.From(new[]
        {
            ReviewState.Draft, ReviewState.Draft, ReviewState.Draft, ReviewState.Approved,
        });

        Assert.Equal(4, summary.Total);
        Assert.Equal(3, summary.Draft);
        Assert.Equal(1, summary.Approved);
        Assert.Equal(0, summary.HandedOff);
    }

    private static ContentSet NewSet() =>
        ContentSet.CreateForRun(
            ContentSetId.New(), Org, Client,
            SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, Guid.NewGuid()), Now).Value;
}
