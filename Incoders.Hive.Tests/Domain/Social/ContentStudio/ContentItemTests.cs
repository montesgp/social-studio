using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Tests.Domain.Social.ContentStudio;

public sealed class ContentItemTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc);

    private static ContentItem NewItem(string text = "original caption")
    {
        var origin = new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "caption");
        var kind = new ContentKind("caption", ContentMedium.Text);
        var draft = ContentDraft.ForText(text, new TargetChannel("instagram", "feed"));
        return ContentItem.Materialize(ContentItemId.New(), Org, Client, origin, kind, draft, Now).Value;
    }

    [Fact]
    public void Materialize_StartsInDraft_WithInitialTrace_AndOwnVariantGroup()
    {
        var id = ContentItemId.New();
        var origin = new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "flyer");
        var kind = new ContentKind("flyer", ContentMedium.Visual);
        var draft = ContentDraft.ForText(null, new TargetChannel("instagram", "feed"));

        var result = ContentItem.Materialize(id, Org, Client, origin, kind, draft, Now);

        Assert.True(result.IsSuccess);
        var item = result.Value;
        Assert.Equal(ReviewState.Draft, item.State);
        Assert.Equal(EditSource.Manual, item.EditTrace.LastEditedSource);
        Assert.Null(item.EditTrace.LastEditedAtUtc);
        Assert.Equal(id.Value, item.VariantGroupId);
        Assert.True(item.IsPrimaryVariant);
        Assert.Null(item.PlanContext);
    }

    [Fact]
    public void Materialize_WithEmptyFormatKey_Fails()
    {
        var origin = new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "");
        var kind = new ContentKind("", ContentMedium.Text);
        var draft = ContentDraft.ForText("x", new TargetChannel("instagram", "feed"));

        var result = ContentItem.Materialize(ContentItemId.New(), Org, Client, origin, kind, draft, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void HappyPath_Draft_To_HandedOff()
    {
        var item = NewItem();

        Assert.True(item.BeginReview(Now).IsSuccess);
        Assert.Equal(ReviewState.InReview, item.State);

        Assert.True(item.Approve(Guid.NewGuid(), Now).IsSuccess);
        Assert.Equal(ReviewState.Approved, item.State);

        Assert.True(item.StageForPublication(Now).IsSuccess);
        Assert.Equal(ReviewState.ReadyForPublication, item.State);

        Assert.True(item.MarkHandedOff(Now).IsSuccess);
        Assert.Equal(ReviewState.HandedOff, item.State);
    }

    [Fact]
    public void StageForPublication_FromDraft_IsIllegalTransition()
    {
        var item = NewItem();

        var result = item.StageForPublication(Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.IllegalReviewTransition, result.Error);
        Assert.Equal(ReviewState.Draft, item.State);
    }

    [Fact]
    public void Approve_FromDraft_Succeeds_ApproveAsGenerated()
    {
        var item = NewItem();
        var reviewer = Guid.NewGuid();

        Assert.True(item.Approve(reviewer, Now).IsSuccess);
        Assert.Equal(ReviewState.Approved, item.State);
        Assert.Equal(reviewer, item.ReviewerUserId);
    }

    [Fact]
    public void HandedOff_IsTerminal_NoFurtherTransition()
    {
        var item = NewItem();
        item.BeginReview(Now);
        item.Approve(Guid.NewGuid(), Now);
        item.StageForPublication(Now);
        item.MarkHandedOff(Now);

        Assert.True(item.BeginReview(Now).IsFailure);
        Assert.True(item.Approve(Guid.NewGuid(), Now).IsFailure);
        Assert.Equal(ReviewState.HandedOff, item.State);
    }

    [Fact]
    public void EditText_UpdatesDraft_SetsEdited_RecordsTrace()
    {
        var item = NewItem("original caption");

        var result = item.EditText("new caption", maxLength: 100, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("new caption", item.Draft.Text);
        Assert.Equal(ReviewState.Edited, item.State);
        Assert.Equal(Now, item.EditTrace.LastEditedAtUtc);
    }

    [Fact]
    public void EditText_Empty_IsRejected_DraftUnchanged()
    {
        var item = NewItem("original caption");

        var result = item.EditText("   ", maxLength: 100, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.TextRequired, result.Error);
        Assert.Equal("original caption", item.Draft.Text);
        Assert.Equal(ReviewState.Draft, item.State);
    }

    [Fact]
    public void EditText_TooLong_IsRejected_DraftUnchanged()
    {
        var item = NewItem("original caption");

        var result = item.EditText("toolong", maxLength: 3, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.TextTooLong, result.Error);
        Assert.Equal("original caption", item.Draft.Text);
    }

    [Fact]
    public void EditText_OnDiscardedItem_IsIllegal()
    {
        var item = NewItem();
        Assert.True(item.Discard(Now).IsSuccess);

        var result = item.EditText("x", maxLength: 100, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.IllegalReviewTransition, result.Error);
    }

    [Fact]
    public void Reject_RequiresReason()
    {
        var item = NewItem();
        item.BeginReview(Now);

        var result = item.Reject(Guid.NewGuid(), "   ", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.RejectionReasonRequired, result.Error);
        Assert.Equal(ReviewState.InReview, item.State);
    }

    [Fact]
    public void Reject_RecordsReasonAndReviewer()
    {
        var reviewer = Guid.NewGuid();
        var item = NewItem();
        item.BeginReview(Now);

        var result = item.Reject(reviewer, "off-brand colors", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReviewState.Rejected, item.State);
        Assert.Equal("off-brand colors", item.RejectionReason);
        Assert.Equal(reviewer, item.ReviewerUserId);
    }

    [Fact]
    public void SpawnVariant_KeepsVariantGroup_StartsDraft_NonPrimary()
    {
        var item = NewItem();
        var original = item.VariantGroupId;

        var variant = item.SpawnVariant(ContentItemId.New(), Now);

        Assert.Equal(original, variant.VariantGroupId);
        Assert.False(variant.IsPrimaryVariant);
        Assert.Equal(ReviewState.Draft, variant.State);
        Assert.NotEqual(item.Id, variant.Id);
    }

    [Fact]
    public void EditVisual_UpdatesMarkupAndAssets_SetsEdited()
    {
        var item = NewItem();
        var assets = new[] { new ContentAssetRef("background", "storage://bg.png", "image/png") };

        var result = item.EditVisual("<spec edited/>", assets, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("<spec edited/>", item.Draft.VisualSpecMarkup);
        Assert.Single(item.Draft.Assets);
        Assert.Equal(ReviewState.Edited, item.State);
        Assert.Equal(Now, item.EditTrace.LastEditedAtUtc);
    }

    [Fact]
    public void EditVisual_OnHandedOffItem_IsIllegal()
    {
        var item = NewItem();
        item.Approve(Guid.NewGuid(), Now);
        item.StageForPublication(Now);
        item.MarkHandedOff(Now);

        var result = item.EditVisual("<x/>", Array.Empty<ContentAssetRef>(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.IllegalReviewTransition, result.Error);
    }

    [Fact]
    public void Duplicate_StartsFresh_InDraft_WithoutReviewerOrReason()
    {
        var item = NewItem("caption A");
        item.BeginReview(Now);
        item.Approve(Guid.NewGuid(), Now);

        var duplicate = item.Duplicate(ContentItemId.New(), Now);

        Assert.Equal(ReviewState.Draft, duplicate.State);
        Assert.Null(duplicate.ReviewerUserId);
        Assert.Null(duplicate.RejectionReason);
        Assert.Equal("caption A", duplicate.Draft.Text);
        Assert.NotEqual(item.Id, duplicate.Id);
    }
}
