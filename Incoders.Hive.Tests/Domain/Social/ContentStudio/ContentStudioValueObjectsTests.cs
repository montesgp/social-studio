using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Tests.Domain.Social.ContentStudio;

public sealed class ContentStudioValueObjectsTests
{
    [Fact]
    public void EditTrace_Initial_IsManualWithNoTimestamp()
    {
        var trace = EditTrace.Initial();

        Assert.Equal(EditSource.Manual, trace.LastEditedSource);
        Assert.Null(trace.LastEditedAtUtc);
        Assert.Null(trace.PromptVersion);
    }

    [Fact]
    public void EditTrace_ManualEdit_RecordsTimestamp()
    {
        var now = new DateTime(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc);

        var trace = EditTrace.ManualEdit(now);

        Assert.Equal(EditSource.Manual, trace.LastEditedSource);
        Assert.Equal(now, trace.LastEditedAtUtc);
    }

    [Fact]
    public void ContentDraft_ForText_HasTextAndChannel_NoAssets()
    {
        var channel = new TargetChannel("instagram", "feed");

        var draft = ContentDraft.ForText("hello", channel);

        Assert.Equal("hello", draft.Text);
        Assert.Null(draft.VisualSpecMarkup);
        Assert.Empty(draft.Assets);
        Assert.Equal(channel, draft.TargetChannel);
    }

    [Fact]
    public void ContentDraft_WithText_ProducesImmutableCopy()
    {
        var draft = ContentDraft.ForText("a", new TargetChannel("tiktok", "reel"));

        var edited = draft with { Text = "b" };

        Assert.Equal("a", draft.Text);
        Assert.Equal("b", edited.Text);
    }

    [Fact]
    public void ContentKind_CarriesFormatKeyAndMedium()
    {
        var kind = new ContentKind("reel-script", ContentMedium.Text);

        Assert.Equal("reel-script", kind.FormatKey);
        Assert.Equal(ContentMedium.Text, kind.Medium);
    }

    [Fact]
    public void ContentAssetRef_HoldsRoleUriMime()
    {
        var asset = new ContentAssetRef("background", "storage://bg.png", "image/png");

        Assert.Equal("background", asset.Role);
        Assert.Equal("storage://bg.png", asset.StorageUri);
        Assert.Equal("image/png", asset.Mime);
    }
}
