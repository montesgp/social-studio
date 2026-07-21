using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Application.Features.Social.Publications;

/// <summary>
/// Editorial gate shared by <c>PublishItemNowCommand</c> and <c>SchedulePublicationCommand</c> (design
/// §6.3, §7): a publication may only be created for an item in <see cref="ReviewState.Approved"/> or
/// <see cref="ReviewState.ReadyForPublication"/>, and v1 supports image-based media only (reels/video
/// are out of scope — PUB-01 restricts to publishable image assets).
/// </summary>
internal static class PublicationEditorialGate
{
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

        return Result.Success();
    }
}
