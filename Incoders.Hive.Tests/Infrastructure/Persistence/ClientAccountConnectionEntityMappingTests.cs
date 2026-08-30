using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ClientAccountConnections;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Infrastructure.Persistence;

/// <summary>
/// EF mapping + repository round-trip tests for <c>social.client_account_connections</c>. Exercises
/// the ciphertext column and the status enum conversion (Restore ↔ persist).
/// </summary>
public sealed class ClientAccountConnectionEntityMappingTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ClientAccountConnection_RoundTrips_CiphertextAndStatus()
    {
        await using var db = CreateDbContext();
        var repo = new EfClientAccountConnectionRepository(db);

        var connection = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, ClientId.New(), SocialNetworkId.New(),
            OrganizationIntegrationId.New(), "ig-business-123", "cipher-text-value", Now.AddDays(60), Now).Value;

        await repo.AddAsync(connection, CancellationToken.None);

        var read = await repo.GetByIdAsync(Org, connection.Id, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal("ig-business-123", read!.ExternalAccountId);
        Assert.Equal("cipher-text-value", read.EncryptedAccessToken);
        Assert.Equal(ClientAccountConnectionStatus.Connected, read.Status);
        Assert.Equal(Now.AddDays(60), read.TokenExpiresAtUtc);
    }

    [Fact]
    public async Task ClientAccountConnection_UpdateAsync_PersistsRotatedTokenAndTestResult()
    {
        await using var db = CreateDbContext();
        var repo = new EfClientAccountConnectionRepository(db);

        var connection = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, ClientId.New(), SocialNetworkId.New(),
            OrganizationIntegrationId.New(), "ig-business-123", "old-cipher", null, Now).Value;
        await repo.AddAsync(connection, CancellationToken.None);

        connection.RotateToken("new-cipher", Now.AddDays(90), Now.AddMinutes(1));
        connection.MarkTestResult(false, "token invalid", Now.AddMinutes(2));
        await repo.UpdateAsync(connection, CancellationToken.None);

        var read = await repo.GetByIdAsync(Org, connection.Id, CancellationToken.None);
        Assert.Equal("new-cipher", read!.EncryptedAccessToken);
        Assert.Equal(ClientAccountConnectionStatus.Failed, read.Status);
        Assert.Equal("token invalid", read.LastError);
    }

    [Fact]
    public async Task GetByClientNetworkAsync_ReturnsTheClientsConnectionForThatNetwork()
    {
        await using var db = CreateDbContext();
        var repo = new EfClientAccountConnectionRepository(db);

        var clientId = ClientId.New();
        var networkId = SocialNetworkId.New();
        var connection = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, clientId, networkId,
            OrganizationIntegrationId.New(), "ig-business-123", "cipher", null, Now).Value;
        await repo.AddAsync(connection, CancellationToken.None);

        var read = await repo.GetByClientNetworkAsync(Org, clientId.Value, networkId, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(connection.Id, read!.Id);
    }

    [Fact]
    public async Task ListByClientAsync_ReturnsAllConnectionsForClient()
    {
        await using var db = CreateDbContext();
        var repo = new EfClientAccountConnectionRepository(db);

        var clientId = ClientId.New();
        var first = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, clientId, SocialNetworkId.New(),
            OrganizationIntegrationId.New(), "ig-1", "cipher-1", null, Now).Value;
        var second = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(), Org, clientId, SocialNetworkId.New(),
            OrganizationIntegrationId.New(), "ig-2", "cipher-2", null, Now).Value;
        await repo.AddAsync(first, CancellationToken.None);
        await repo.AddAsync(second, CancellationToken.None);

        var results = await repo.ListByClientAsync(Org, clientId.Value, CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"client-account-connections-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
