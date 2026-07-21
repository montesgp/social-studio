using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ClientAccountConnections;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Tests.Application.Social.Publications;

/// <summary>
/// WU2 (Phase 5) TDD tests for <see cref="PublicationDestinationResolver"/> — the two-level credential
/// resolution service (design §6.2). All fakes, no network/DB access.
/// </summary>
public sealed class PublicationDestinationResolverTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly ClientAccountConnectionId ConnectionId = ClientAccountConnectionId.New();
    private static readonly SocialNetworkId NetworkId = SocialNetworkId.New();

    [Fact]
    public async Task ResolveAsync_BothLevelsConnected_ReturnsUnprotectedCredentials()
    {
        var connection = BuildConnection();
        var connections = new FakeClientAccountConnectionRepository(connection);
        var integrationResolver = new FakeIntegrationResolver(IntegrationStatus.Connected);
        var protector = new FakeProtector();

        var resolver = new PublicationDestinationResolver(connections, integrationResolver, protector);
        var destination = new PublicationDestination(ConnectionId.Value, NetworkId.Value, "instagram");

        var result = await resolver.ResolveAsync(OrganizationId, destination, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ig-business-account-1", result.Value.ExternalAccountId);
        Assert.Equal("unprotected:ciphertext", result.Value.AccessToken);
        Assert.Equal("instagram", result.Value.NetworkKey);
    }

    [Fact]
    public async Task ResolveAsync_ConnectionMissing_ReturnsDestinationUnresolved()
    {
        var connections = new FakeClientAccountConnectionRepository(connection: null);
        var integrationResolver = new FakeIntegrationResolver(IntegrationStatus.Connected);
        var protector = new FakeProtector();

        var resolver = new PublicationDestinationResolver(connections, integrationResolver, protector);
        var destination = new PublicationDestination(ConnectionId.Value, NetworkId.Value, "instagram");

        var result = await resolver.ResolveAsync(OrganizationId, destination, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.DestinationUnresolved.Code, result.Error.Code);
        Assert.False(protector.WasCalled);
    }

    [Fact]
    public async Task ResolveAsync_OrgIntegrationNotConnected_ReturnsDestinationUnresolved()
    {
        var connection = BuildConnection();
        var connections = new FakeClientAccountConnectionRepository(connection);
        // Simulates ResolveProviderAsync's own IntegrationNotConnected failure — the resolver must
        // not duplicate that status check, only propagate it as DestinationUnresolved.
        var integrationResolver = new FakeIntegrationResolver(failWith: IntegrationErrors.IntegrationNotConnected);
        var protector = new FakeProtector();

        var resolver = new PublicationDestinationResolver(connections, integrationResolver, protector);
        var destination = new PublicationDestination(ConnectionId.Value, NetworkId.Value, "instagram");

        var result = await resolver.ResolveAsync(OrganizationId, destination, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.DestinationUnresolved.Code, result.Error.Code);
        Assert.False(protector.WasCalled);
    }

    [Fact]
    public async Task ResolveAsync_ConnectionDisabled_ReturnsDestinationUnresolved()
    {
        var connection = BuildConnection();
        Assert.True(connection.Disable(DateTime.UtcNow).IsSuccess);
        var connections = new FakeClientAccountConnectionRepository(connection);
        var integrationResolver = new FakeIntegrationResolver(IntegrationStatus.Connected);
        var protector = new FakeProtector();

        var resolver = new PublicationDestinationResolver(connections, integrationResolver, protector);
        var destination = new PublicationDestination(ConnectionId.Value, NetworkId.Value, "instagram");

        var result = await resolver.ResolveAsync(OrganizationId, destination, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.DestinationUnresolved.Code, result.Error.Code);
        Assert.False(protector.WasCalled);
    }

    [Fact]
    public async Task ResolveAsync_ConnectionInFailedStatus_ReturnsDestinationUnresolved()
    {
        var connection = BuildConnection();
        connection.MarkTestResult(isSuccess: false, error: "provider rejected", DateTime.UtcNow);
        var connections = new FakeClientAccountConnectionRepository(connection);
        var integrationResolver = new FakeIntegrationResolver(IntegrationStatus.Connected);
        var protector = new FakeProtector();

        var resolver = new PublicationDestinationResolver(connections, integrationResolver, protector);
        var destination = new PublicationDestination(ConnectionId.Value, NetworkId.Value, "instagram");

        var result = await resolver.ResolveAsync(OrganizationId, destination, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PublicationErrors.DestinationUnresolved.Code, result.Error.Code);
        Assert.False(protector.WasCalled);
    }

    private static ClientAccountConnection BuildConnection()
    {
        var created = ClientAccountConnection.Create(
            ConnectionId,
            OrganizationId,
            new ClientId(ClientId),
            NetworkId,
            OrganizationIntegrationId.New(),
            "ig-business-account-1",
            "ciphertext",
            tokenExpiresAtUtc: null,
            DateTime.UtcNow);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private sealed class FakeClientAccountConnectionRepository : IClientAccountConnectionRepository
    {
        private readonly ClientAccountConnection? _connection;

        public FakeClientAccountConnectionRepository(ClientAccountConnection? connection)
        {
            _connection = connection;
        }

        public Task<ClientAccountConnection?> GetByIdAsync(
            Guid organizationId, ClientAccountConnectionId id, CancellationToken ct) =>
            Task.FromResult(_connection is not null && organizationId == OrganizationId ? _connection : null);

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

    private sealed class FakeIntegrationResolver : IIntegrationResolver
    {
        private readonly IntegrationStatus? _status;
        private readonly Error? _failWith;

        public FakeIntegrationResolver(IntegrationStatus status)
        {
            _status = status;
        }

        public FakeIntegrationResolver(Error failWith)
        {
            _failWith = failWith;
        }

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId, string providerKey, CancellationToken cancellationToken = default)
        {
            if (_failWith is { } error)
            {
                return Task.FromResult(Result.Failure<ResolvedIntegration>(error));
            }

            var resolved = new ResolvedIntegration(
                organizationId,
                providerKey,
                "Instagram",
                IntegrationProviderCategory.Publishing,
                IntegrationConnectionMode.BringYourOwnKey,
                _status!.Value,
                IsReadOnly: false,
                SelectedAiModel: null,
                Credentials: new Dictionary<string, string>());

            return Task.FromResult(Result.Success(resolved));
        }
    }

    private sealed class FakeProtector : IIntegrationSecretProtector
    {
        public bool WasCalled { get; private set; }

        public string Protect(string value) => $"protected:{value}";

        public string Unprotect(string protectedValue)
        {
            WasCalled = true;
            return $"unprotected:{protectedValue}";
        }
    }
}
