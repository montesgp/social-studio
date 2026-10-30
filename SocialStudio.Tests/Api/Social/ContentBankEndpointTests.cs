using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Publications;

namespace SocialStudio.Tests.Api.Social;

/// <summary>
/// WU3 (Phase 11) smoke tests for the Content Bank / publish / schedule endpoint contract (design §9).
///
/// <para>
/// Following this codebase's "Api" test convention (see <c>PublicationConnectionEndpointTests</c>) —
/// no <c>WebApplicationFactory</c> harness exists in this suite. The endpoint mapping itself is a
/// thin org-scoping adapter over already-tested Application handlers
/// (<c>ListClientContentBankQueryTests</c>, <c>PublishItemNowCommandTests</c>,
/// <c>SchedulePublicationCommandTests</c>). What IS Api-surface-specific and worth asserting directly
/// here is the error → HTTP-status contract: <c>PublicationErrors.ItemNotPublishable</c> and
/// <c>PublicationErrors.IllegalPublicationTransition</c> must be <see cref="ErrorType.Conflict"/> so
/// the shared <c>ResultExtensions.ToProblem</c> mapping used by every endpoint in this codebase turns
/// them into HTTP 409, per design §9.
/// </para>
/// </summary>
public sealed class ContentBankEndpointTests
{
    [Fact]
    public void ItemNotPublishable_MapsToConflict_409()
    {
        Assert.Equal(ErrorType.Conflict, PublicationErrors.ItemNotPublishable.Type);
        Assert.Equal("social.publications.item_not_publishable", PublicationErrors.ItemNotPublishable.Code);
    }

    [Fact]
    public void DestinationUnresolved_MapsToConflict_409()
    {
        Assert.Equal(ErrorType.Conflict, PublicationErrors.DestinationUnresolved.Type);
        Assert.Equal("social.publications.destination_unresolved", PublicationErrors.DestinationUnresolved.Code);
    }

    [Fact]
    public void IllegalPublicationTransition_MapsToConflict_409()
    {
        Assert.Equal(ErrorType.Conflict, PublicationErrors.IllegalPublicationTransition.Type);
        Assert.Equal("social.publications.illegal_transition", PublicationErrors.IllegalPublicationTransition.Code);
    }
}
