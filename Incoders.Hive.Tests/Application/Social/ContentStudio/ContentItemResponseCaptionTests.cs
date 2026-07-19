using Incoders.Hive.Application.Features.Social.ContentStudio;
using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Tests.Application.Social.ContentStudio;

/// <summary>
/// TDD test for task 3.9 (Content Studio editing completeness): <see cref="ContentItemResponse"/>
/// surfaces the dedicated <c>Caption</c> field so the web DTO (<c>content-studio.ts</c>) can expose
/// it alongside <c>Text</c>.
/// </summary>
public sealed class ContentItemResponseCaptionTests
{
    [Fact]
    public void FromDomain_VisualItemWithCaption_SurfacesCaptionValue()
    {
        var caption = Caption.Create("Grow your brand today. #growth").Value;
        var draft = new ContentDraft(null, caption, "<spec/>", Array.Empty<ContentAssetRef>(), new TargetChannel("instagram", "carousel"));
        var item = ContentItem.Restore(
            ContentItemId.New(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "carousel"),
            new ContentKind("carousel", ContentMedium.Visual),
            draft,
            ReviewState.Draft,
            EditTrace.Initial(),
            Guid.NewGuid(),
            true,
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var response = ContentItemResponse.FromDomain(item);

        Assert.Equal("Grow your brand today. #growth", response.Caption);
        Assert.Null(response.Text);
    }

    [Fact]
    public void FromDomain_TextItemWithNoCaption_CaptionIsNull()
    {
        var draft = ContentDraft.ForText("Reel script", new TargetChannel("instagram", "reel"));
        var item = ContentItem.Restore(
            ContentItemId.New(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "reel-script"),
            new ContentKind("reel-script", ContentMedium.Text),
            draft,
            ReviewState.Draft,
            EditTrace.Initial(),
            Guid.NewGuid(),
            true,
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var response = ContentItemResponse.FromDomain(item);

        Assert.Null(response.Caption);
        Assert.Equal("Reel script", response.Text);
    }
}
