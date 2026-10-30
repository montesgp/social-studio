using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.Publications.Connections;
using SocialStudio.Domain.Social.ClientAccountConnections;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Tenants;

namespace SocialStudio.Tests.Api.Social;

/// <summary>
/// WU2 (Phase 8) tests for the publication-connection management endpoint surface.
///
/// <para>
/// This codebase's "Api" test convention (see <c>SocialClientsEndpointPolicyTests</c>,
/// <c>ClientBrandManualsEndpointRoleTests</c>) unit-tests the Application/Api contract directly
/// rather than spinning up a full <c>WebApplicationFactory</c> — no such harness exists in this
/// suite. These tests follow the same convention: the org-admin role gate
/// (<see cref="ClientAccountConnectionAuthorization"/>) is exercised directly, and the response DTO's token-free
/// contract is asserted (cross-organization denial is covered by the org-scoped repository lookups
/// already tested in <see cref="SocialStudio.Tests.Application.Social.Publications.ClientAccountConnectionCommandsTests"/>).
/// </para>
/// </summary>
public sealed class PublicationConnectionEndpointTests
{
    private static readonly TenantId TenantId = new(Guid.NewGuid());
    private static readonly Guid UserId = Guid.NewGuid();

    // ─── Org-admin gate: create/rotate/test are role-restricted ───────────────

    [Theory]
    [InlineData(TenantRole.Owner, true)]
    [InlineData(TenantRole.Admin, true)]
    [InlineData(TenantRole.Member, false)]
    public async Task IsOrgAdminAsync_GatesByTenantRole(TenantRole role, bool expectedAllowed)
    {
        var membership = new FakeTenantMembershipService(role);

        var allowed = await ClientAccountConnectionAuthorization.IsOrgAdminAsync(membership, TenantId, UserId, CancellationToken.None);

        Assert.Equal(expectedAllowed, allowed);
    }

    [Fact]
    public async Task IsOrgAdminAsync_NoMembership_Denies()
    {
        var membership = new FakeTenantMembershipService(role: null);

        var allowed = await ClientAccountConnectionAuthorization.IsOrgAdminAsync(membership, TenantId, UserId, CancellationToken.None);

        Assert.False(allowed);
    }

    // ─── Create command carries the plaintext token for server-side protection, response never does ───

    [Fact]
    public void CreateClientAccountConnectionCommand_CarriesPlaintextAccessToken_ForServerSideProtection()
    {
        var command = new CreateClientAccountConnectionCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "ig-account-1", "raw-token-over-tls", TokenExpiresAtUtc: null);

        Assert.Equal("raw-token-over-tls", command.AccessToken);
    }

    [Fact]
    public void ClientAccountConnectionResponse_FromDomain_IsTokenFree()
    {
        var connection = ClientAccountConnection.Create(
            ClientAccountConnectionId.New(),
            Guid.NewGuid(),
            new ClientId(Guid.NewGuid()),
            new SocialNetworkId(Guid.NewGuid()),
            new OrganizationIntegrationId(Guid.NewGuid()),
            "ig-account-1",
            "ciphertext",
            tokenExpiresAtUtc: null,
            DateTime.UtcNow).Value;

        var response = ClientAccountConnectionResponse.FromDomain(connection, network: null);

        var properties = response.GetType().GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(properties, name => name.Contains("AccessToken", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeTenantMembershipService : ITenantMembershipService
    {
        private readonly TenantRole? _role;

        public FakeTenantMembershipService(TenantRole? role)
        {
            _role = role;
        }

        public Task<bool> IsMemberAsync(TenantId tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_role is not null);

        public Task<TenantRole?> GetRoleAsync(TenantId tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_role);

        public Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<TenantMembership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantMembership>>([]);
    }
}
