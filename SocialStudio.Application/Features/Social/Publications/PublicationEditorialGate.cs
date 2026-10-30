using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Domain.Social.Publications;

namespace SocialStudio.Application.Features.Social.Publications;

/// <summary>
/// Editorial gate shared by <c>PublishItemNowCommand</c> and <c>SchedulePublicationCommand</c> (design
/// §6.3, §7): a publication may only be created for an item in <see cref="ReviewState.Approved"/> or
/// <see cref="ReviewState.ReadyForPublication"/>, and v1 supports image-based media only (reels/video
/// are out of scope — PUB-01 restricts to publishable image assets).
/// </summary>
internal static class PublicationEditorialGate
{
    /// <summary>
    /// Whether a medium can be delivered to a social network publishing API at all. Script/copy
    /// deliverables (<see cref="ContentMedium.Text"/>) and documents are download-only, and video is
    /// out of scope for v1. Exposed so the Content Bank projection advertises the SAME rule this gate
    /// enforces — a divergence there would show a "Publish" affordance the server always rejects.
    /// </summary>
    public static bool IsPublishableMedium(ContentMedium medium) => medium is ContentMedium.Visual;

    public static Result EnsurePublishable(ContentItem item)
    {
        if (item.State is not (ReviewState.Approved or ReviewState.ReadyForPublication))
        {
            return Result.Failure(PublicationErrors.ItemNotPublishable);
        }

        if (item.Kind.Medium == ContentMedium.Video)
        {
            return Result.Failure(PublicationErrors.Validation(
                "Reels/video publishing is not supported in v1; only image-based content can be published."));
        }

        // Script/copy formats (reel-script, hook, caption, content-idea…) are operator deliverables:
        // they exist to be read and downloaded, and there is no network API to post them to on their
        // own. Only Visual media is publishable.
        if (!IsPublishableMedium(item.Kind.Medium))
        {
            return Result.Failure(PublicationErrors.Validation(
                "This format is a script/text deliverable and cannot be published to a social network. Download it instead."));
        }

        return Result.Success();
    }
}
