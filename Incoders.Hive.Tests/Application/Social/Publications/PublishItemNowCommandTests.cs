using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications.PublishNow;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientAccountConnections;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Application.Social.Publications;

/// <summary>WU3 (Phase 9) TDD tests for <see cref="PublishItemNowCommand"/> (design §6.3, §7).</summary>
public sealed class PublishItemNowCommandTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();
    private static readonly Guid NetworkId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_ItemNotPublishable_ReturnsErrorAndCreatesNoPublication()
    {
        var item = BuildItem(ReviewState.Draft);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        var connections = new FakeClientAccountConnectionRepository(BuildConnection(ClientId));
        var runner = BuildRunner(succeed: true, externalReference: "media-1");
        var handler = new PublishItemNowCommandHandler(items, publications, connections, runner, new FixedClock(Now));

        var result = await handler.Handle(BuildCommand(item.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.ItemNotPublishable.Code, result.Error.Code);
        Assert.Empty(publications.Added);
        Assert.False(items.WasUpdated);
    }

    [Fact]
    public async Task Handle_TextMediumItem_IsRejected_EvenWhenApproved()
    {
        // reel-script and other Script/Text formats are operator deliverables: downloadable, never
        // posted to a network API. The editorial gate blocked Video but let Text through, so an
        // approved script could be pushed to Instagram as if it were a visual piece.
        var item = BuildItem(ReviewState.Approved, ContentMedium.Text);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        var connections = new FakeClientAccountConnectionRepository(BuildConnection(ClientId));
        var runner = BuildRunner(succeed: true, externalReference: "media-1");
        var handler = new PublishItemNowCommandHandler(items, publications, connections, runner, new FixedClock(Now));

        var result = await handler.Handle(BuildCommand(item.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(publications.Added);
        Assert.False(items.WasUpdated);
    }

    [Fact]
    public async Task Handle_HappyPath_PersistsBeforeCallingConnectorThenMarksPublished()
    {
        var item = BuildItem(ReviewState.Approved);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        var connections = new FakeClientAccountConnectionRepository(BuildConnection(ClientId));
        var runner = BuildRunner(succeed: true, externalReference: "media-1");
        var handler = new PublishItemNowCommandHandler(items, publications, connections, runner, new FixedClock(Now));

        var result = await handler.Handle(BuildCommand(item.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.Published.ToString(), result.Value.Status);
        Assert.Equal("media-1", result.Value.ExternalReference);

        // Persist-before-call: the aggregate was added (in Publishing) BEFORE the connector ran, then
        // updated once more after the outcome (Published).
        Assert.Single(publications.Added);
        Assert.True(publications.AddedBeforeConnectorCall);
        Assert.NotEmpty(publications.Updated);
    }

    [Fact]
    public async Task Handle_ConnectorFailure_MarksFailedAndPersists()
    {
        var item = BuildItem(ReviewState.ReadyForPublication);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        var connections = new FakeClientAccountConnectionRepository(BuildConnection(ClientId));
        var runner = BuildRunner(succeed: false, externalReference: null);
        var handler = new PublishItemNowCommandHandler(items, publications, connections, runner, new FixedClock(Now));

        var result = await handler.Handle(BuildCommand(item.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.PublishFailed.ToString(), result.Value.Status);
        Assert.Single(publications.Added);
        Assert.NotEmpty(publications.Updated);
    }

    [Fact]
    public async Task Handle_ReadyForPublicationState_IsAlsoActionable()
    {
        var item = BuildItem(ReviewState.ReadyForPublication);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        var connections = new FakeClientAccountConnectionRepository(BuildConnection(ClientId));
        var runner = BuildRunner(succeed: true, externalReference: "media-2");
        var handler = new PublishItemNowCommandHandler(items, publications, connections, runner, new FixedClock(Now));

        var result = await handler.Handle(BuildCommand(item.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ConnectionBelongsToDifferentClient_ReturnsDestinationUnresolvedAndCreatesNoPublication()
    {
        var item = BuildItem(ReviewState.Approved);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        // Connection belongs to a DIFFERENT client than the item — must be rejected before any
        // Publication is created or the connector is invoked.
        var connections = new FakeClientAccountConnectionRepository(BuildConnection(Guid.NewGuid()));
        var runner = BuildRunner(succeed: true, externalReference: "media-1");
        var handler = new PublishItemNowCommandHandler(items, publications, connections, runner, new FixedClock(Now));

        var result = await handler.Handle(BuildCommand(item.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.DestinationUnresolved.Code, result.Error.Code);
        Assert.Empty(publications.Added);
    }

    [Fact]
    public async Task Handle_HappyPath_AttributesPublicationToItemClientNotRequestClientId()
    {
        var item = BuildItem(ReviewState.Approved);
        var items = new FakeContentItemRepository(item);
        var publications = new FakePublicationRepository();
        var connections = new FakeClientAccountConnectionRepository(BuildConnection(ClientId));
        var runner = BuildRunner(succeed: true, externalReference: "media-1");
        var handler = new PublishItemNowCommandHandler(items, publications, connections, runner, new FixedClock(Now));

        // Command carries a different (bogus) request.ClientId — the handler must ignore it and use
        // the item's real ClientId for the Publication.
        var command = new PublishItemNowCommand(
            OrganizationId, Guid.NewGuid(), item.Id.Value, ConnectionId, NetworkId, "instagram");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(publications.Added);
        Assert.Equal(item.ClientId, publications.Added[0].ClientId);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static PublishItemNowCommand BuildCommand(Guid itemId) =>
        new(OrganizationId, ClientId, itemId, ConnectionId, NetworkId, "instagram");

    private static ClientAccountConnection BuildConnection(Guid clientId)
    {
        var created = ClientAccountConnection.Create(
            new ClientAccountConnectionId(ConnectionId),
            OrganizationId,
            new ClientId(clientId),
            new SocialNetworkId(NetworkId),
            Incoders.Hive.Domain.Integrations.OrganizationIntegrationId.New(),
            "ig-business-account-1",
            "ciphertext",
            tokenExpiresAtUtc: null,
            Now);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static ContentItem BuildItem(ReviewState state) => BuildItem(state, ContentMedium.Visual);

    private static ContentItem BuildItem(ReviewState state, ContentMedium medium)
    {
        var draft = new ContentDraft(
            null,
            Caption.Create("Hello world").Value,
            null,
            [new ContentAssetRef("rendered", "org/client/analysis/slide_1.png", "image/png")],
            new TargetChannel("instagram", "feed"));

        var created = ContentItem.Materialize(
            ContentItemId.New(),
            OrganizationId,
            ClientId,
            new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "ig_feed_carousel"),
            new ContentKind("ig_feed_carousel", medium),
            draft,
            Now);

        Assert.True(created.IsSuccess);
        var item = created.Value;

        if (state == ReviewState.Approved)
        {
            Assert.True(item.Approve(Guid.NewGuid(), Now).IsSuccess);
        }
        else if (state == ReviewState.ReadyForPublication)
        {
            Assert.True(item.Approve(Guid.NewGuid(), Now).IsSuccess);
            Assert.True(item.StageForPublication(Now).IsSuccess);
        }

        return item;
    }

    private static PublicationPublishAttemptRunner BuildRunner(bool succeed, string? externalReference)
    {
        var resolver = new FakeDestinationResolver(succeed: true);
        var selector = new FakeConnectorSelector(succeed, externalReference);
        var storage = new FakeAssetStorage();
        return new PublicationPublishAttemptRunner(resolver, selector, storage);
    }

    private sealed class FakeClientAccountConnectionRepository : IClientAccountConnectionRepository
    {
        private readonly ClientAccountConnection? _connection;

        public FakeClientAccountConnectionRepository(ClientAccountConnection? connection) => _connection = connection;

        public Task<ClientAccountConnection?> GetByIdAsync(
            Guid organizationId, ClientAccountConnectionId id, CancellationToken ct) =>
            Task.FromResult(_connection);

        public Task<IReadOnlyList<ClientAccountConnection>> ListByClientAsync(
            Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClientAccountConnection>>(
                _connection is null ? [] : [_connection]);

        public Task<ClientAccountConnection?> GetByClientNetworkAsync(
            Guid organizationId, Guid clientId, SocialNetworkId socialNetworkId, CancellationToken ct) =>
            Task.FromResult(_connection);

        public Task AddAsync(ClientAccountConnection connection, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(ClientAccountConnection connection, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeContentItemRepository : IContentItemRepository
    {
        private readonly ContentItem _item;

        public bool WasUpdated { get; private set; }

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

        public Task UpdateAsync(ContentItem item, CancellationToken ct)
        {
            WasUpdated = true;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid organizationId, ContentItemId id, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakePublicationRepository : IPublicationRepository
    {
        public List<Publication> Added { get; } = [];
        public List<Publication> Updated { get; } = [];
        public bool AddedBeforeConnectorCall { get; set; }

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
            AddedBeforeConnectorCall = true;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Publication publication, CancellationToken ct)
        {
            Updated.Add(publication);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDestinationResolver : IPublicationDestinationResolver
    {
        private readonly bool _succeed;

        public FakeDestinationResolver(bool succeed) => _succeed = succeed;

        public Task<Result<ResolvedPublicationCredentials>> ResolveAsync(
            Guid organizationId, PublicationDestination destination, CancellationToken ct) =>
            Task.FromResult(_succeed
                ? Result.Success(new ResolvedPublicationCredentials("ig-account-1", "token", "instagram"))
                : Result.Failure<ResolvedPublicationCredentials>(PublicationErrors.DestinationUnresolved));
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

    private sealed class FixedClock : Incoders.Hive.Application.Abstractions.IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }
}
