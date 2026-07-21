using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications.Connections;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ClientAccountConnections;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Application.Social.Publications;

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
        var handler = new CreateClientAccountConnectionCommandHandler(connections, protector, new FixedClock(Now));

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
        var handler = new RotateClientAccountTokenCommandHandler(connections, protector, new FixedClock(Now));

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
        var handler = new TestClientAccountConnectionCommandHandler(connections, resolver, new FixedClock(Now));

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
        var handler = new TestClientAccountConnectionCommandHandler(connections, resolver, new FixedClock(Now));

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
        var handler = new ListClientAccountConnectionsQueryHandler(connections);

        var result = await handler.Handle(
            new ListClientAccountConnectionsQuery(OrganizationId, ClientId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);

        var response = result.Value[0];
        var properties = response.GetType().GetProperties().Select(p => p.Name);
        // TokenExpiresAtUtc (metadata) is fine — only the raw/access token value itself must be absent.
        Assert.DoesNotContain(properties, name => name.Contains("AccessToken", StringComparison.OrdinalIgnoreCase));
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
