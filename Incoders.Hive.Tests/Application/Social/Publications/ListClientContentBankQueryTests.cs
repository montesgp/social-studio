using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications.ListClientContentBank;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Application.Social.Publications;

/// <summary>WU3 (Phase 10) TDD tests for <see cref="ListClientContentBankQuery"/> (design §5.2, CB-01..CB-03).</summary>
public sealed class ListClientContentBankQueryTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_ReturnsClientItemsWithAttachedPublicationSummaries()
    {
        var item = BuildItem();
        var items = new FakeContentItemRepository([item]);
        var publication = BuildPublication(item.Id.Value);
        var publications = new FakePublicationRepository([publication]);
        var handler = new ListClientContentBankQueryHandler(items, publications);

        var result = await handler.Handle(
            new ListClientContentBankQuery(OrganizationId, ClientId, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var response = result.Value[0];
        Assert.Equal(item.Id.Value, response.Id);
        Assert.Single(response.Publications);
        Assert.Equal(publication.Id.Value, response.Publications[0].Id);
    }

    /// <summary>
    /// The Bank response must carry the item's OWNING set id so "Open in Studio" loads the existing
    /// set instead of re-materializing.
    ///
    /// Ownership is resolved through SET MEMBERSHIP, not through the item's
    /// <c>Origin.SourceRunId</c>. That id is the generation JOB id, while a GenerationRun set is
    /// keyed by a run id derived from (analysisId, clientId) — the two never match, so the previous
    /// by-run lookup returned null for every item and the client fell through to a re-materialize
    /// path that could not succeed ("This analysis produced no successful formats to materialize").
    /// </summary>
    [Fact]
    public async Task Handle_ResolvesOwningSetId_ThroughSetMembership()
    {
        var item = BuildItem();
        var items = new FakeContentItemRepository([item]);
        var publications = new FakePublicationRepository([]);

        // Deliberately give the set a run id that is NOT the item's SourceRunId — that mismatch is
        // exactly the production shape, and membership must still resolve it.
        var set = ContentSet.CreateForRun(
            ContentSetId.New(), OrganizationId, ClientId,
            SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, Guid.NewGuid()), Now).Value;
        Assert.True(set.AddMember(item.Id, 0).IsSuccess);

        var sets = new FakeContentSetRepository([set]);
        var handler = new ListClientContentBankQueryHandler(items, publications, sets: sets);

        var result = await handler.Handle(
            new ListClientContentBankQuery(OrganizationId, ClientId, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(set.Id.Value, result.Value[0].SetId);
    }

    [Fact]
    public async Task Handle_ItemBelongingToNoSet_LeavesSetIdNull()
    {
        var item = BuildItem();
        var items = new FakeContentItemRepository([item]);
        var publications = new FakePublicationRepository([]);
        var set = ContentSet.CreateForRun(
            ContentSetId.New(), OrganizationId, ClientId,
            SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, item.Origin.SourceRunId), Now).Value;
        var sets = new FakeContentSetRepository([set]);
        var handler = new ListClientContentBankQueryHandler(items, publications, sets: sets);

        var result = await handler.Handle(
            new ListClientContentBankQuery(OrganizationId, ClientId, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Matching the set's run id is NOT enough — only membership counts.
        Assert.Null(result.Value[0].SetId);
    }

    [Fact]
    public async Task Handle_NoOwningSet_LeavesSetIdNull()
    {
        var item = BuildItem();
        var items = new FakeContentItemRepository([item]);
        var publications = new FakePublicationRepository([]);
        var sets = new FakeContentSetRepository([]);
        var handler = new ListClientContentBankQueryHandler(items, publications, sets: sets);

        var result = await handler.Handle(
            new ListClientContentBankQuery(OrganizationId, ClientId, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value[0].SetId);
    }

    [Fact]
    public async Task Handle_ForwardsFilterToRepository()
    {
        var items = new FakeContentItemRepository([]);
        var publications = new FakePublicationRepository([]);
        var handler = new ListClientContentBankQueryHandler(items, publications);

        var from = Now.AddDays(-7);
        var to = Now;
        await handler.Handle(
            new ListClientContentBankQuery(OrganizationId, ClientId, from, to, "Approved"), CancellationToken.None);

        Assert.NotNull(items.LastFilter);
        Assert.Equal(from, items.LastFilter!.FromUtc);
        Assert.Equal(to, items.LastFilter.ToUtc);
        Assert.Equal(ReviewState.Approved, items.LastFilter.State);
    }

    [Fact]
    public async Task Handle_NoDateRange_StillReturnsFullHistory()
    {
        var item = BuildItem();
        var items = new FakeContentItemRepository([item]);
        var publications = new FakePublicationRepository([]);
        var handler = new ListClientContentBankQueryHandler(items, publications);

        var result = await handler.Handle(
            new ListClientContentBankQuery(OrganizationId, ClientId, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Null(items.LastFilter!.FromUtc);
        Assert.Null(items.LastFilter.ToUtc);
    }

    private static ContentItem BuildItem()
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
        return created.Value;
    }

    private static Publication BuildPublication(Guid contentItemId)
    {
        var destination = new PublicationDestination(Guid.NewGuid(), Guid.NewGuid(), "instagram");
        var created = Publication.PublishNow(PublicationId.New(), OrganizationId, ClientId, contentItemId, destination, Now);
        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private sealed class FakeContentItemRepository : IContentItemRepository
    {
        private readonly IReadOnlyList<ContentItem> _items;

        public ContentBankFilter? LastFilter { get; private set; }

        public FakeContentItemRepository(IReadOnlyList<ContentItem> items) => _items = items;

        public Task<ContentItem?> GetByIdAsync(Guid organizationId, ContentItemId id, CancellationToken ct) =>
            Task.FromResult<ContentItem?>(null);

        public Task<IReadOnlyList<ContentItem>> ListByRunAsync(Guid organizationId, Guid sourceRunId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentItem>>([]);

        public Task<IReadOnlyList<ContentItem>> ListBySetAsync(Guid organizationId, ContentSetId setId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentItem>>([]);

        public Task<IReadOnlyList<ContentItem>> ListByClientAsync(
            Guid organizationId, Guid clientId, ContentBankFilter filter, CancellationToken ct)
        {
            LastFilter = filter;
            return Task.FromResult(_items);
        }

        public Task AddRangeAsync(IReadOnlyList<ContentItem> items, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(ContentItem item, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(Guid organizationId, ContentItemId id, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeContentSetRepository : IContentSetRepository
    {
        private readonly IReadOnlyList<ContentSet> _sets;

        public FakeContentSetRepository(IReadOnlyList<ContentSet> sets) => _sets = sets;

        public Task<ContentSet?> GetByIdAsync(Guid organizationId, ContentSetId id, CancellationToken ct) =>
            Task.FromResult(_sets.FirstOrDefault(s => s.Id == id));

        public Task<ContentSet?> GetByRunAsync(Guid organizationId, Guid sourceRunId, CancellationToken ct) =>
            Task.FromResult(_sets.FirstOrDefault(s => s.Origin.SourceRunId == sourceRunId));

        /// <summary>Resolves ownership through set membership, the way the real repository does.</summary>
        public Task<IReadOnlyDictionary<Guid, Guid>> GetSetIdsByItemIdsAsync(
            Guid organizationId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
        {
            var map = new Dictionary<Guid, Guid>();
            foreach (var set in _sets)
            {
                foreach (var member in set.Members)
                {
                    if (itemIds.Contains(member.ItemId.Value))
                    {
                        map.TryAdd(member.ItemId.Value, set.Id.Value);
                    }
                }
            }

            return Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(map);
        }

        public Task AddAsync(ContentSet set, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(ContentSet set, CancellationToken ct) => Task.CompletedTask;

        public Task<ReviewSummary> GetReviewSummaryAsync(Guid organizationId, ContentSetId setId, CancellationToken ct) =>
            Task.FromResult(ReviewSummary.Empty);
    }

    private sealed class FakePublicationRepository : IPublicationRepository
    {
        private readonly IReadOnlyList<Publication> _publications;

        public FakePublicationRepository(IReadOnlyList<Publication> publications) => _publications = publications;

        public Task<Publication?> GetByIdAsync(Guid organizationId, PublicationId id, CancellationToken ct) =>
            Task.FromResult<Publication?>(null);

        public Task<IReadOnlyList<Publication>> ListByContentItemsAsync(
            Guid organizationId, IReadOnlyList<Guid> contentItemIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Publication>>(
                _publications.Where(p => contentItemIds.Contains(p.ContentItemId)).ToList());

        public Task<IReadOnlyList<Publication>> ListByClientAsync(Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Publication>>([]);

        public Task AddAsync(Publication publication, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(Publication publication, CancellationToken ct) => Task.CompletedTask;
    }
}
