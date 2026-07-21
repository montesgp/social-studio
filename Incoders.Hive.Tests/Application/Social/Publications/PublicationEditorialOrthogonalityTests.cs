using Incoders.Hive.Application.Abstractions;
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

/// <summary>
/// WU3 (Phase 12) orthogonality assertion (PUB-06, design §12): a full publish cycle
/// (<c>PublishItemNowCommand</c> → <c>MarkPublished</c>) must leave the source <see cref="ContentItem"/>
/// row byte-for-byte unchanged — same <see cref="ReviewState"/>, same <see cref="ContentDraft"/>. The
/// publication axis reads the item read-only via <see cref="IContentItemRepository.GetByIdAsync"/> and
/// never calls <see cref="IContentItemRepository.UpdateAsync"/>.
/// </summary>
public sealed class PublicationEditorialOrthogonalityTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid ConnectionId = Guid.NewGuid();
    private static readonly Guid NetworkId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PublishNow_HappyPath_NeverCallsUpdateAsyncOnContentItemRepository()
    {
        var item = BuildApprovedItem();
        var originalState = item.State;
        var originalDraft = item.Draft;
        var originalUpdatedAtUtc = item.UpdatedAtUtc;

        var items = new SpyContentItemRepository(item);
        var publications = new NoopPublicationRepository();
        var connections = new NoopClientAccountConnectionRepository(BuildConnection());
        var runner = new PublicationPublishAttemptRunner(
            new SucceedingDestinationResolver(), new SucceedingConnectorSelector(), new NoopAssetStorage());
        var handler = new PublishItemNowCommandHandler(items, publications, connections, runner, new FixedClock(Now));

        var result = await handler.Handle(
            new PublishItemNowCommand(OrganizationId, ClientId, item.Id.Value, ConnectionId, NetworkId, "instagram"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PublicationStatus.Published.ToString(), result.Value.Status);

        // The publication axis reads the item read-only and never persists a change back to it.
        Assert.False(items.UpdateAsyncWasCalled);

        // The item row itself is byte-for-byte unchanged: same ReviewState, same ContentDraft, same
        // UpdatedAtUtc — the publish cycle never touched it.
        Assert.Equal(originalState, item.State);
        Assert.Equal(originalDraft, item.Draft);
        Assert.Equal(originalUpdatedAtUtc, item.UpdatedAtUtc);
    }

    private static ClientAccountConnection BuildConnection()
    {
        var created = ClientAccountConnection.Create(
            new ClientAccountConnectionId(ConnectionId),
            OrganizationId,
            new ClientId(ClientId),
            new SocialNetworkId(NetworkId),
            Incoders.Hive.Domain.Integrations.OrganizationIntegrationId.New(),
            "ig-business-account-1",
            "ciphertext",
            tokenExpiresAtUtc: null,
            Now);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static ContentItem BuildApprovedItem()
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
        Assert.True(item.Approve(Guid.NewGuid(), Now).IsSuccess);
        return item;
    }

    private sealed class SpyContentItemRepository : IContentItemRepository
    {
        private readonly ContentItem _item;

        public bool UpdateAsyncWasCalled { get; private set; }

        public SpyContentItemRepository(ContentItem item) => _item = item;

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
            UpdateAsyncWasCalled = true;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid organizationId, ContentItemId id, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoopClientAccountConnectionRepository : IClientAccountConnectionRepository
    {
        private readonly ClientAccountConnection _connection;

        public NoopClientAccountConnectionRepository(ClientAccountConnection connection) => _connection = connection;

        public Task<ClientAccountConnection?> GetByIdAsync(
            Guid organizationId, ClientAccountConnectionId id, CancellationToken ct) =>
            Task.FromResult<ClientAccountConnection?>(_connection);

        public Task<IReadOnlyList<ClientAccountConnection>> ListByClientAsync(
            Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClientAccountConnection>>([_connection]);

        public Task<ClientAccountConnection?> GetByClientNetworkAsync(
            Guid organizationId, Guid clientId, SocialNetworkId socialNetworkId, CancellationToken ct) =>
            Task.FromResult<ClientAccountConnection?>(_connection);

        public Task AddAsync(ClientAccountConnection connection, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(ClientAccountConnection connection, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoopPublicationRepository : IPublicationRepository
    {
        public Task<Publication?> GetByIdAsync(Guid organizationId, PublicationId id, CancellationToken ct) =>
            Task.FromResult<Publication?>(null);

        public Task<IReadOnlyList<Publication>> ListByContentItemsAsync(
            Guid organizationId, IReadOnlyList<Guid> contentItemIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Publication>>([]);

        public Task<IReadOnlyList<Publication>> ListByClientAsync(Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Publication>>([]);

        public Task AddAsync(Publication publication, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(Publication publication, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class SucceedingDestinationResolver : IPublicationDestinationResolver
    {
        public Task<Result<ResolvedPublicationCredentials>> ResolveAsync(
            Guid organizationId, PublicationDestination destination, CancellationToken ct) =>
            Task.FromResult(Result.Success(new ResolvedPublicationCredentials("ig-account-1", "token", "instagram")));
    }

    private sealed class SucceedingConnectorSelector : IPublicationConnectorSelector
    {
        public Result<IPublicationConnector> For(string networkKey) =>
            Result.Success<IPublicationConnector>(new SucceedingConnector());
    }

    private sealed class SucceedingConnector : IPublicationConnector
    {
        public string NetworkKey => "instagram";

        public Task<Result<PublicationResult>> PublishAsync(ResolvedPublicationRequest request, CancellationToken ct) =>
            Task.FromResult(Result.Success(new PublicationResult("media-1")));
    }

    private sealed class NoopAssetStorage : IGeneratedAssetStorage
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
