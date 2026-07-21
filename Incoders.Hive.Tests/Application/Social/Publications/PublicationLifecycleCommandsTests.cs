using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications.Cancel;
using Incoders.Hive.Application.Features.Social.Publications.GetPublication;
using Incoders.Hive.Application.Features.Social.Publications.Reclaim;
using Incoders.Hive.Application.Features.Social.Publications.Retry;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Application.Social.Publications;

/// <summary>WU3 (Phase 9) TDD tests for Retry/Cancel/Reclaim/GetPublication (design §6.3).</summary>
public sealed class PublicationLifecycleCommandsTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid ContentItemId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    // ─── Retry ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_FromPublishFailed_ReRunsConnectorAndMarksPublished()
    {
        var publication = BuildFailedPublication();
        var publications = new FakePublicationRepository(publication);
        var items = new FakeContentItemRepository(BuildItem());
        var runner = BuildRunner(succeed: true, "media-retry-1");
        var handler = new RetryPublicationCommandHandler(publications, items, runner, new FixedClock(Now.AddMinutes(5)));

        var result = await handler.Handle(
            new RetryPublicationCommand(OrganizationId, publication.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.Published.ToString(), result.Value.Status);
        Assert.Equal(2, publication.AttemptCount);
    }

    [Fact]
    public async Task Retry_NotInPublishFailed_ReturnsIllegalTransition()
    {
        var publication = BuildScheduledPublication();
        var publications = new FakePublicationRepository(publication);
        var items = new FakeContentItemRepository(BuildItem());
        var runner = BuildRunner(succeed: true, "media-x");
        var handler = new RetryPublicationCommandHandler(publications, items, runner, new FixedClock(Now));

        var result = await handler.Handle(
            new RetryPublicationCommand(OrganizationId, publication.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.IllegalPublicationTransition.Code, result.Error.Code);
    }

    // ─── Cancel ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_FromScheduled_Succeeds()
    {
        var publication = BuildScheduledPublication();
        var publications = new FakePublicationRepository(publication);
        var handler = new CancelPublicationCommandHandler(publications, new FixedClock(Now));

        var result = await handler.Handle(
            new CancelPublicationCommand(OrganizationId, publication.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.Cancelled.ToString(), result.Value.Status);
    }

    [Fact]
    public async Task Cancel_FromPublished_ReturnsIllegalTransition()
    {
        var publication = BuildPublishedPublication();
        var publications = new FakePublicationRepository(publication);
        var handler = new CancelPublicationCommandHandler(publications, new FixedClock(Now));

        var result = await handler.Handle(
            new CancelPublicationCommand(OrganizationId, publication.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.IllegalPublicationTransition.Code, result.Error.Code);
    }

    // ─── Reclaim ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reclaim_StaleOpenAttempt_MovesToPublishFailed()
    {
        var publication = BuildPublishingPublication(attemptStartedAtUtc: Now);
        var publications = new FakePublicationRepository(publication);
        var handler = new ReclaimStuckPublicationCommandHandler(publications, new FixedClock(Now.AddMinutes(30)));

        var result = await handler.Handle(
            new ReclaimStuckPublicationCommand(OrganizationId, publication.Id.Value, "worker crash"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.PublishFailed.ToString(), result.Value.Status);
    }

    [Fact]
    public async Task Reclaim_WithinStalenessThreshold_ReturnsValidationError_CannotRaceInFlightPublish()
    {
        var publication = BuildPublishingPublication(attemptStartedAtUtc: Now);
        var publications = new FakePublicationRepository(publication);
        // Only 1 minute elapsed — well within the staleness threshold; a real publish may still be in flight.
        var handler = new ReclaimStuckPublicationCommandHandler(publications, new FixedClock(Now.AddMinutes(1)));

        var result = await handler.Handle(
            new ReclaimStuckPublicationCommand(OrganizationId, publication.Id.Value, "premature"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationStatus.Publishing, publication.Status);
    }

    // ─── GetPublication ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublication_Found_ReturnsResponse()
    {
        var publication = BuildScheduledPublication();
        var publications = new FakePublicationRepository(publication);
        var handler = new GetPublicationQueryHandler(publications);

        var result = await handler.Handle(
            new GetPublicationQuery(OrganizationId, publication.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(publication.Id.Value, result.Value.Id);
    }

    [Fact]
    public async Task GetPublication_NotFound_ReturnsNotFound()
    {
        var publications = new FakePublicationRepository(existing: null);
        var handler = new GetPublicationQueryHandler(publications);

        var result = await handler.Handle(
            new GetPublicationQuery(OrganizationId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static PublicationDestination BuildDestination() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "instagram");

    private static Publication BuildScheduledPublication()
    {
        var created = Publication.Schedule(
            PublicationId.New(), OrganizationId, ClientId, ContentItemId, BuildDestination(),
            Now.AddDays(1), Now);
        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static Publication BuildFailedPublication()
    {
        var created = Publication.PublishNow(
            PublicationId.New(), OrganizationId, ClientId, ContentItemId, BuildDestination(), Now);
        Assert.True(created.IsSuccess);
        var publication = created.Value;
        Assert.True(publication.MarkFailed("provider rejected", Now).IsSuccess);
        return publication;
    }

    private static Publication BuildPublishedPublication()
    {
        var created = Publication.PublishNow(
            PublicationId.New(), OrganizationId, ClientId, ContentItemId, BuildDestination(), Now);
        Assert.True(created.IsSuccess);
        var publication = created.Value;
        Assert.True(publication.MarkPublished("media-1", Now).IsSuccess);
        return publication;
    }

    private static Publication BuildPublishingPublication(DateTime attemptStartedAtUtc)
    {
        var created = Publication.PublishNow(
            PublicationId.New(), OrganizationId, ClientId, ContentItemId, BuildDestination(), attemptStartedAtUtc);
        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static ContentItem BuildItem()
    {
        var draft = new ContentDraft(
            null, Caption.Create("Hello world").Value, null,
            [new ContentAssetRef("rendered", "org/client/analysis/slide_1.png", "image/png")],
            new TargetChannel("instagram", "feed"));

        var created = ContentItem.Materialize(
            new ContentItemId(ContentItemId), OrganizationId, ClientId,
            new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "ig_feed_carousel"),
            new ContentKind("ig_feed_carousel", ContentMedium.Visual), draft, Now);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static PublicationPublishAttemptRunner BuildRunner(bool succeed, string? externalReference)
    {
        var resolver = new FakeDestinationResolver();
        var selector = new FakeConnectorSelector(succeed, externalReference);
        var storage = new FakeAssetStorage();
        return new PublicationPublishAttemptRunner(resolver, selector, storage);
    }

    private sealed class FakePublicationRepository : IPublicationRepository
    {
        private readonly Publication? _existing;

        public FakePublicationRepository(Publication? existing) => _existing = existing;

        public Task<Publication?> GetByIdAsync(Guid organizationId, PublicationId id, CancellationToken ct) =>
            Task.FromResult(_existing is not null && _existing.Id == id ? _existing : null);

        public Task<IReadOnlyList<Publication>> ListByContentItemsAsync(
            Guid organizationId, IReadOnlyList<Guid> contentItemIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Publication>>([]);

        public Task<IReadOnlyList<Publication>> ListByClientAsync(Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Publication>>([]);

        public Task AddAsync(Publication publication, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(Publication publication, CancellationToken ct) => Task.CompletedTask;
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

    private sealed class FakeDestinationResolver : IPublicationDestinationResolver
    {
        public Task<Result<ResolvedPublicationCredentials>> ResolveAsync(
            Guid organizationId, PublicationDestination destination, CancellationToken ct) =>
            Task.FromResult(Result.Success(new ResolvedPublicationCredentials("ig-account-1", "token", "instagram")));
    }

    private sealed class FakeConnectorSelector : IPublicationConnectorSelector
    {
        private readonly bool _succeed;
        private readonly string? _externalReference;

        public FakeConnectorSelector(bool succeed, string? externalReference)
        {
            _succeed = succeed;
            _externalReference = externalReference;
        }

        public Result<IPublicationConnector> For(string networkKey) =>
            Result.Success<IPublicationConnector>(new FakeConnector(_succeed, _externalReference));
    }

    private sealed class FakeConnector : IPublicationConnector
    {
        private readonly bool _succeed;
        private readonly string? _externalReference;

        public FakeConnector(bool succeed, string? externalReference)
        {
            _succeed = succeed;
            _externalReference = externalReference;
        }

        public string NetworkKey => "instagram";

        public Task<Result<PublicationResult>> PublishAsync(ResolvedPublicationRequest request, CancellationToken ct) =>
            Task.FromResult(_succeed
                ? Result.Success(new PublicationResult(_externalReference!))
                : Result.Failure<PublicationResult>(PublicationErrors.ConnectorFailure("provider rejected")));
    }

    private sealed class FakeAssetStorage : IGeneratedAssetStorage
    {
        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;

        public Result<string> TryGetPublicUrl(string storagePath) =>
            Result<string>.Success($"https://cdn.example.com/{storagePath}");
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }
}
