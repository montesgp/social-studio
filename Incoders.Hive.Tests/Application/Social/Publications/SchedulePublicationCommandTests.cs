using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications.Schedule;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Application.Social.Publications;

/// <summary>WU3 (Phase 9) TDD tests for <see cref="SchedulePublicationCommand"/> (design §6.3, OQ-2).</summary>
public sealed class SchedulePublicationCommandTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();
    private static readonly Guid NetworkId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_ItemNotPublishable_ReturnsError()
    {
        var item = BuildItem(ReviewState.Draft);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        var handler = new SchedulePublicationCommandHandler(items, publications, new FixedClock(Now));

        var result = await handler.Handle(
            new SchedulePublicationCommand(
                OrganizationId, ClientId, item.Id.Value, ConnectionId, NetworkId, "instagram",
                Now.AddDays(1)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.ItemNotPublishable.Code, result.Error.Code);
        Assert.Empty(publications.Added);
    }

    [Fact]
    public async Task Handle_HappyPath_CreatesScheduledPublicationWithoutCallingAnyConnector()
    {
        var item = BuildItem(ReviewState.Approved);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        var handler = new SchedulePublicationCommandHandler(items, publications, new FixedClock(Now));

        var scheduledFor = Now.AddDays(1);
        var result = await handler.Handle(
            new SchedulePublicationCommand(
                OrganizationId, ClientId, item.Id.Value, ConnectionId, NetworkId, "instagram", scheduledFor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.Scheduled.ToString(), result.Value.Status);
        Assert.Equal(scheduledFor, result.Value.ScheduledForUtc);
        Assert.Single(publications.Added);
    }

    [Fact]
    public async Task Handle_PastScheduledTime_ReturnsValidationError()
    {
        var item = BuildItem(ReviewState.Approved);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        var handler = new SchedulePublicationCommandHandler(items, publications, new FixedClock(Now));

        var result = await handler.Handle(
            new SchedulePublicationCommand(
                OrganizationId, ClientId, item.Id.Value, ConnectionId, NetworkId, "instagram",
                Now.AddMinutes(-1)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(publications.Added);
    }

    private static ContentItem BuildItem(ReviewState state)
    {
        var draft = new ContentDraft(
            null, Caption.Create("Hello world").Value, null,
            [new ContentAssetRef("rendered", "org/client/analysis/slide_1.png", "image/png")],
            new TargetChannel("instagram", "feed"));

        var created = ContentItem.Materialize(
            ContentItemId.New(), OrganizationId, ClientId,
            new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "ig_feed_carousel"),
            new ContentKind("ig_feed_carousel", ContentMedium.Visual), draft, Now);

        Assert.True(created.IsSuccess);
        var item = created.Value;

        if (state == ReviewState.Approved)
        {
            Assert.True(item.Approve(Guid.NewGuid(), Now).IsSuccess);
        }

        return item;
    }

    private sealed class FakeContentItemRepository : IContentItemRepository
    {
        private readonly ContentItem _item;

        public FakeContentItemRepository(ContentItem item) => _item = item;

        public Task<ContentItem?> GetByIdAsync(Guid organizationId, ContentItemId id, CancellationToken ct) =>
            Task.FromResult(_item.Id == id ? _item : null);

        public Task<IReadOnlyList<ContentItem>> ListByRunAsync(Guid organizationId, Guid sourceRunId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentItem>>([]);

        public Task<IReadOnlyList<ContentItem>> ListBySetAsync(Guid organizationId, ContentSetId setId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentItem>>([]);

        public Task<IReadOnlyList<ContentItem>> ListByClientAsync(
            Guid organizationId, Guid clientId, ContentBankFilter filter, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentItem>>([]);

        public Task AddRangeAsync(IReadOnlyList<ContentItem> items, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(ContentItem item, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(Guid organizationId, ContentItemId id, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakePublicationRepository : IPublicationRepository
    {
        public List<Publication> Added { get; } = [];

        public Task<Publication?> GetByIdAsync(Guid organizationId, PublicationId id, CancellationToken ct) =>
            Task.FromResult<Publication?>(null);

        public Task<IReadOnlyList<Publication>> ListByContentItemsAsync(
            Guid organizationId, IReadOnlyList<Guid> contentItemIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Publication>>([]);

        public Task<IReadOnlyList<Publication>> ListByClientAsync(Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Publication>>([]);

        public Task AddAsync(Publication publication, CancellationToken ct)
        {
            Added.Add(publication);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Publication publication, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedClock : Incoders.Hive.Application.Abstractions.IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }
}
