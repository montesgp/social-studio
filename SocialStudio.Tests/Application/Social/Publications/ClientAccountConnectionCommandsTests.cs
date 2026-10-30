using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Application.Features.Social.Publications.Connections;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ClientAccountConnections;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using SocialStudio.Domain.Social.Publications;

namespace SocialStudio.Tests.Application.Social.Publications;

/// <summary>WU2 (Phase 7) TDD tests for the ClientAccountConnection management command slices.</summary>
public sealed class ClientAccountConnectionCommandsTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid SocialNetworkId = Guid.NewGuid();
    private static readonly Guid OrganizationIntegrationId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    // ─── Create: protects the plaintext BEFORE persistence ────────────────────

    [Fact]
    public async Task Create_ProtectsPlaintextBeforeConstructingAggregate()
    {
        var connections = new FakeClientAccountConnectionRepository();
        var protector = new FakeProtector();
        var handler = new CreateClientAccountConnectionCommandHandler(connections, new FakeSocialNetworkRepository(BuildNetwork()), protector, new FixedClock(Now));

        var command = new CreateClientAccountConnectionCommand(
            OrganizationId, ClientId, SocialNetworkId, OrganizationIntegrationId,
            "ig-account-1", "plaintext-token", TokenExpiresAtUtc: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("plaintext-token", protector.ProtectedValues);
        Assert.NotNull(connections.Added);
        // The persisted aggregate only ever holds ciphertext, never the raw plaintext.
        Assert.NotEqual("plaintext-token", connections.Added!.EncryptedAccessToken);
        Assert.StartsWith("protected:", connections.Added!.EncryptedAccessToken);
    }

    // ─── Rotate: re-protects and replaces ciphertext ───────────────────────────

    [Fact]
    public async Task Rotate_ReplacesCiphertext()
    {
        var existing = BuildConnection();
        var connections = new FakeClientAccountConnectionRepository(existing);
        var protector = new FakeProtector();
        var handler = new RotateClientAccountTokenCommandHandler(connections, new FakeSocialNetworkRepository(BuildNetwork()), protector, new FixedClock(Now));

        var command = new RotateClientAccountTokenCommand(
            OrganizationId, existing.Id.Value, "new-plaintext-token", TokenExpiresAtUtc: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("new-plaintext-token", protector.ProtectedValues);
        Assert.NotNull(connections.Updated);
        Assert.NotEqual("new-plaintext-token", connections.Updated!.EncryptedAccessToken);
    }

    // ─── Test: records MarkTestResult outcome ──────────────────────────────────

    [Fact]
    public async Task Test_ResolvableDestination_RecordsSuccess()
    {
        var existing = BuildConnection();
        var connections = new FakeClientAccountConnectionRepository(existing);
        var resolver = new FakeDestinationResolver(succeed: true);
        var handler = new TestClientAccountConnectionCommandHandler(connections, new FakeSocialNetworkRepository(BuildNetwork()), resolver, new FixedClock(Now));

        var result = await handler.Handle(
            new TestClientAccountConnectionCommand(OrganizationId, existing.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClientAccountConnectionStatus.Connected.ToString(), result.Value.Status);
    }

    [Fact]
    public async Task Test_UnresolvableDestination_RecordsFailure()
    {
        var existing = BuildConnection();
        var connections = new FakeClientAccountConnectionRepository(existing);
        var resolver = new FakeDestinationResolver(succeed: false);
        var handler = new TestClientAccountConnectionCommandHandler(connections, new FakeSocialNetworkRepository(BuildNetwork()), resolver, new FixedClock(Now));

        var result = await handler.Handle(
            new TestClientAccountConnectionCommand(OrganizationId, existing.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClientAccountConnectionStatus.Failed.ToString(), result.Value.Status);
    }

    // ─── List: token-free response ──────────────────────────────────────────────

    [Fact]
    public async Task List_ResponseNeverIncludesToken()
    {
        var existing = BuildConnection();
        var connections = new FakeClientAccountConnectionRepository(existing);
        var handler = new ListClientAccountConnectionsQueryHandler(connections, new FakeSocialNetworkRepository(BuildNetwork()));

        var result = await handler.Handle(
            new ListClientAccountConnectionsQuery(OrganizationId, ClientId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);

        var response = result.Value[0];
        var properties = response.GetType().GetProperties().Select(p => p.Name);
        // TokenExpiresAtUtc (metadata) is fine — only the raw/access token value itself must be absent.
        Assert.DoesNotContain(properties, name => name.Contains("AccessToken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_ProjectsTheNetworkCode_SoCallersNeverHardcodeANetworkKey()
    {
        // The projection used to expose only an opaque SocialNetworkId, which forced the publish UI
        // to send a hardcoded "instagram" network key for EVERY destination — publishing through the
        // wrong connector as soon as a second network existed.
        var connections = new FakeClientAccountConnectionRepository(BuildConnection());
        var handler = new ListClientAccountConnectionsQueryHandler(
            connections, new FakeSocialNetworkRepository(BuildNetwork()));

        var result = await handler.Handle(
            new ListClientAccountConnectionsQuery(OrganizationId, ClientId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("instagram", result.Value[0].NetworkKey);
        Assert.Equal("Instagram", result.Value[0].NetworkDisplayName);
    }

    [Fact]
    public async Task List_UnresolvableNetwork_YieldsEmptyKeyInsteadOfThrowing()
    {
        var connections = new FakeClientAccountConnectionRepository(BuildConnection());
        var handler = new ListClientAccountConnectionsQueryHandler(
            connections, new FakeSocialNetworkRepository(network: null));

        var result = await handler.Handle(
            new ListClientAccountConnectionsQuery(OrganizationId, ClientId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value[0].NetworkKey);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ClientAccountConnection BuildConnection()
    {
        var created = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(),
            OrganizationId,
            new ClientId(ClientId),
            new SocialNetworkId(SocialNetworkId),
            new OrganizationIntegrationId(OrganizationIntegrationId),
            "ig-account-1",
            "existing-ciphertext",
            tokenExpiresAtUtc: null,
            Now);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    /// <summary>Resolves the one network the fixture's connections point at, so the projection can
    /// carry its code instead of the caller hardcoding a network key.</summary>
    private sealed class FakeSocialNetworkRepository : ISocialNetworkRepository
    {
        private readonly SocialNetwork? _network;

        public FakeSocialNetworkRepository(SocialNetwork? network = null) => _network = network;

        public Task<SocialNetwork?> GetByIdAsync(SocialNetworkId id, CancellationToken cancellationToken) =>
            Task.FromResult(_network is not null && _network.Id == id ? _network : null);

        public Task<IReadOnlyList<SocialNetwork>> GetByIdsAsync(
            IReadOnlyCollection<SocialNetworkId> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SocialNetwork>>(
                _network is not null && ids.Contains(_network.Id) ? [_network] : []);

        public Task<SocialNetwork?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult<SocialNetwork?>(null);

        public Task<PageResult<SocialNetwork>> ListAsync(
            SocialNetworkListQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PageResult<SocialNetwork>([], 0, 1, 20));

        public Task AddAsync(SocialNetwork network, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Update(SocialNetwork network) { }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static SocialNetwork BuildNetwork() => SocialNetwork.Restore(
        new SocialNetworkId(SocialNetworkId), "instagram", "Instagram", "instagram", isActive: true, Now, Now);

    private sealed class FakeClientAccountConnectionRepository : IClientAccountConnectionRepository
    {
        private readonly ClientAccountConnection? _existing;

        public ClientAccountConnection? Added { get; private set; }
        public ClientAccountConnection? Updated { get; private set; }

        public FakeClientAccountConnectionRepository(ClientAccountConnection? existing = null)
        {
            _existing = existing;
        }

        public Task<ClientAccountConnection?> GetByIdAsync(
            Guid organizationId, ClientAccountConnectionId id, CancellationToken ct) =>
            Task.FromResult(_existing is not null && _existing.Id == id ? _existing : null);

        public Task<IReadOnlyList<ClientAccountConnection>> ListByClientAsync(
            Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClientAccountConnection>>(
                _existing is null ? [] : [_existing]);

        public Task<ClientAccountConnection?> GetByClientNetworkAsync(
            Guid organizationId, Guid clientId, SocialNetworkId socialNetworkId, CancellationToken ct) =>
            Task.FromResult(_existing);

        public Task AddAsync(ClientAccountConnection connection, CancellationToken ct)
        {
            Added = connection;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ClientAccountConnection connection, CancellationToken ct)
        {
            Updated = connection;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProtector : IIntegrationSecretProtector
    {
        public List<string> ProtectedValues { get; } = [];

        public string Protect(string value)
        {
            ProtectedValues.Add(value);
            return $"protected:{value}";
        }

        public string Unprotect(string protectedValue) => protectedValue.Replace("protected:", string.Empty);
    }

    private sealed class FakeDestinationResolver : IPublicationDestinationResolver
    {
        private readonly bool _succeed;

        public FakeDestinationResolver(bool succeed)
        {
            _succeed = succeed;
        }

        public Task<Result<ResolvedPublicationCredentials>> ResolveAsync(
            Guid organizationId, PublicationDestination destination, CancellationToken ct) =>
            Task.FromResult(_succeed
                ? Result.Success(new ResolvedPublicationCredentials("ig-account-1", "token", "instagram"))
                : Result.Failure<ResolvedPublicationCredentials>(PublicationErrors.DestinationUnresolved));
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }
}
