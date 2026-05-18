using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Activate;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Create;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Deactivate;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.GetById;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.GetByClient;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Update;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Social.ClientReferentAccounts;

public sealed class ClientReferentAccountLifecycleTests
{
    private static readonly DateTime Now = new(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 5, 18, 13, 0, 0, DateTimeKind.Utc);

    // --- Create ---

    [Fact]
    public async Task CreateHandler_ValidCommand_ReturnsResponseWithDenormalizedNetworkFields()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();

        var result = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", "@brand", "Main IG", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("instagram", result.Value.SocialNetworkCode);
        Assert.Equal("Instagram", result.Value.SocialNetworkDisplayName);
        Assert.Equal("instagram", result.Value.SocialNetworkIconKey);
        Assert.Equal("https://www.instagram.com/brand", result.Value.ProfileUrl);
        Assert.Equal("@brand", result.Value.Handle);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task CreateHandler_ClientNotFoundInOrg_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (_, orgId, networkId) = await harness.SeedActiveClientAndNetwork();

        var result = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, Guid.NewGuid(), networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.not_found", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_ClientInDifferentOrg_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, _, networkId) = await harness.SeedActiveClientAndNetwork();

        var result = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                Guid.NewGuid() /* different org */, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.not_found", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_ClientInactive_ReturnsValidationError()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        harness.ClientRepository.SetInactive(clientId, orgId);

        var result = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task CreateHandler_SocialNetworkNotFound_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, _) = await harness.SeedActiveClientAndNetwork();

        var result = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, Guid.NewGuid(),
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.not_found", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_SocialNetworkInactive_ReturnsSocialNetworkInactive()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        harness.NetworkRepository.SetInactive(new SocialNetworkId(networkId));

        var result = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.social_network_inactive", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_DuplicateProfileUrl_ReturnsProfileUrlAlreadyExists()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();

        await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        var result = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.profile_url_already_exists", result.Error.Code);
    }

    // --- GetById ---

    [Fact]
    public async Task GetByIdHandler_ExistingAccount_ReturnsResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var created = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        var result = await harness.GetById.Handle(
            new GetClientReferentAccountByIdQuery(orgId, clientId, created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.Value.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetByIdHandler_WrongOrg_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var created = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        var result = await harness.GetById.Handle(
            new GetClientReferentAccountByIdQuery(Guid.NewGuid(), clientId, created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetByIdHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, _) = await harness.SeedActiveClientAndNetwork();

        var result = await harness.GetById.Handle(
            new GetClientReferentAccountByIdQuery(orgId, clientId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.not_found", result.Error.Code);
    }

    // --- List ---

    [Fact]
    public async Task ListHandler_ActiveOnly_ReturnsOnlyActiveAccounts()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var r1 = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand1", null, null, null),
            CancellationToken.None);
        await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand2", null, null, null),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateClientReferentAccountCommand(orgId, clientId, r1.Value.Id),
            CancellationToken.None);

        var result = await harness.List.Handle(
            new ListClientReferentAccountsByClientQuery(orgId, clientId, false, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("https://www.instagram.com/brand2", result.Value.Items[0].ProfileUrl);
    }

    [Fact]
    public async Task ListHandler_IncludeInactive_ReturnsAllAccounts()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var r1 = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand1", null, null, null),
            CancellationToken.None);
        await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand2", null, null, null),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateClientReferentAccountCommand(orgId, clientId, r1.Value.Id),
            CancellationToken.None);

        var result = await harness.List.Handle(
            new ListClientReferentAccountsByClientQuery(orgId, clientId, true, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task ListHandler_OrgScoped_ExcludesAccountsFromOtherOrg()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand1", null, null, null),
            CancellationToken.None);

        // Seed a client in org2 and create an account for it
        var (clientId2, orgId2, networkId2) = await harness.SeedActiveClientAndNetwork();
        await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId2, clientId2, networkId2,
                "https://www.instagram.com/brand2", null, null, null),
            CancellationToken.None);

        var result = await harness.List.Handle(
            new ListClientReferentAccountsByClientQuery(orgId, clientId, false, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("https://www.instagram.com/brand1", result.Value.Items[0].ProfileUrl);
    }

    [Fact]
    public async Task ListHandler_OrderedByCreatedAtUtcDesc()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();

        harness.Clock.UtcNow = Now;
        await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand1", null, null, null),
            CancellationToken.None);

        harness.Clock.UtcNow = Later;
        await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand2", null, null, null),
            CancellationToken.None);

        var result = await harness.List.Handle(
            new ListClientReferentAccountsByClientQuery(orgId, clientId, false, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        // brand2 (Later) should come first (DESC)
        Assert.Equal("https://www.instagram.com/brand2", result.Value.Items[0].ProfileUrl);
        Assert.Equal("https://www.instagram.com/brand1", result.Value.Items[1].ProfileUrl);
    }

    // --- Update ---

    [Fact]
    public async Task UpdateHandler_ValidCommand_ReturnsUpdatedResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var created = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", "@brand", null, null),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateClientReferentAccountCommand(
                orgId, clientId, created.Value.Id,
                "https://www.instagram.com/brand2", "@brand2", "Label", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://www.instagram.com/brand2", result.Value.ProfileUrl);
        Assert.Equal("@brand2", result.Value.Handle);
        Assert.Equal("Label", result.Value.Label);
    }

    [Fact]
    public async Task UpdateHandler_ProfileUrlChanges_RecheckUniqueness_UniqueSucceeds()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var created = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateClientReferentAccountCommand(
                orgId, clientId, created.Value.Id,
                "https://www.instagram.com/brand-new", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://www.instagram.com/brand-new", result.Value.ProfileUrl);
    }

    [Fact]
    public async Task UpdateHandler_ProfileUrlChanges_RecheckUniqueness_DuplicateFails()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand1", null, null, null),
            CancellationToken.None);
        var r2 = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand2", null, null, null),
            CancellationToken.None);

        // Try to update r2 to use brand1's URL
        var result = await harness.Update.Handle(
            new UpdateClientReferentAccountCommand(
                orgId, clientId, r2.Value.Id,
                "https://www.instagram.com/brand1", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.profile_url_already_exists", result.Error.Code);
    }

    [Fact]
    public async Task UpdateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, _) = await harness.SeedActiveClientAndNetwork();

        var result = await harness.Update.Handle(
            new UpdateClientReferentAccountCommand(
                orgId, clientId, Guid.NewGuid(),
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.not_found", result.Error.Code);
    }

    [Fact]
    public async Task UpdateHandler_WrongOrg_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var created = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateClientReferentAccountCommand(
                Guid.NewGuid(), clientId, created.Value.Id,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.not_found", result.Error.Code);
    }

    // --- Deactivate ---

    [Fact]
    public async Task DeactivateHandler_ActiveAccount_ReturnsDeactivatedResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var created = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateClientReferentAccountCommand(orgId, clientId, created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task DeactivateHandler_AlreadyInactive_ReturnsAlreadyInactive()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var created = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateClientReferentAccountCommand(orgId, clientId, created.Value.Id),
            CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateClientReferentAccountCommand(orgId, clientId, created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.already_inactive", result.Error.Code);
    }

    [Fact]
    public async Task DeactivateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, _) = await harness.SeedActiveClientAndNetwork();

        var result = await harness.Deactivate.Handle(
            new DeactivateClientReferentAccountCommand(orgId, clientId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.not_found", result.Error.Code);
    }

    // --- Activate ---

    [Fact]
    public async Task ActivateHandler_InactiveAccount_ReturnsActivatedResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var created = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateClientReferentAccountCommand(orgId, clientId, created.Value.Id),
            CancellationToken.None);

        var result = await harness.Activate.Handle(
            new ActivateClientReferentAccountCommand(orgId, clientId, created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task ActivateHandler_AlreadyActive_ReturnsAlreadyActive()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, networkId) = await harness.SeedActiveClientAndNetwork();
        var created = await harness.Create.Handle(
            new CreateClientReferentAccountCommand(
                orgId, clientId, networkId,
                "https://www.instagram.com/brand", null, null, null),
            CancellationToken.None);

        var result = await harness.Activate.Handle(
            new ActivateClientReferentAccountCommand(orgId, clientId, created.Value.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.already_active", result.Error.Code);
    }

    [Fact]
    public async Task ActivateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId, _) = await harness.SeedActiveClientAndNetwork();

        var result = await harness.Activate.Handle(
            new ActivateClientReferentAccountCommand(orgId, clientId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_referent_accounts.not_found", result.Error.Code);
    }

    // --- Harness ---

    private sealed class Harness
    {
        public Harness(DateTime nowUtc)
        {
            Clock = new TestClock(nowUtc);
            ClientRepository = new InMemorySocialClientRepository();
            NetworkRepository = new InMemorySocialNetworkRepository();
            var accountRepository = new InMemoryClientReferentAccountRepository();

            Create = new CreateClientReferentAccountHandler(accountRepository, ClientRepository, NetworkRepository, Clock);
            GetById = new GetClientReferentAccountByIdHandler(accountRepository, NetworkRepository);
            List = new ListClientReferentAccountsByClientHandler(accountRepository, ClientRepository, NetworkRepository);
            Update = new UpdateClientReferentAccountHandler(accountRepository, NetworkRepository, Clock);
            Deactivate = new DeactivateClientReferentAccountHandler(accountRepository, NetworkRepository, Clock);
            Activate = new ActivateClientReferentAccountHandler(accountRepository, NetworkRepository, Clock);
        }

        public TestClock Clock { get; }
        public InMemorySocialClientRepository ClientRepository { get; }
        public InMemorySocialNetworkRepository NetworkRepository { get; }

        public CreateClientReferentAccountHandler Create { get; }
        public GetClientReferentAccountByIdHandler GetById { get; }
        public ListClientReferentAccountsByClientHandler List { get; }
        public UpdateClientReferentAccountHandler Update { get; }
        public DeactivateClientReferentAccountHandler Deactivate { get; }
        public ActivateClientReferentAccountHandler Activate { get; }

        /// <summary>Seeds one active client + one active social network. Returns (clientId, orgId, networkId) as Guids.</summary>
        public async Task<(Guid ClientId, Guid OrgId, Guid NetworkId)> SeedActiveClientAndNetwork()
        {
            var orgId = Guid.NewGuid();
            var clientId = ClientId.New();
            ClientRepository.AddClient(Client.Restore(
                clientId, orgId, "Test Client", null, true,
                Clock.UtcNow, Clock.UtcNow));

            var networkId = SocialNetworkId.New();
            var network = SocialNetwork.Restore(
                networkId, "instagram", "Instagram", "instagram", true,
                Clock.UtcNow, Clock.UtcNow);
            NetworkRepository.AddNetwork(network);

            await Task.CompletedTask;
            return (clientId.Value, orgId, networkId.Value);
        }
    }

    // --- InMemory Repositories ---

    internal sealed class InMemoryClientReferentAccountRepository : IClientReferentAccountRepository
    {
        private readonly Dictionary<ClientReferentAccountId, ClientReferentAccount> _store = new();
        private readonly HashSet<(Guid ClientId, Guid NetworkId, string ProfileUrl)> _uniqueIndex = new();

        public Task<ClientReferentAccount?> GetByIdAsync(
            ClientReferentAccountId id,
            Guid organizationId,
            CancellationToken ct)
        {
            _store.TryGetValue(id, out var account);
            if (account is null || account.OrganizationId != organizationId)
            {
                return Task.FromResult<ClientReferentAccount?>(null);
            }
            return Task.FromResult<ClientReferentAccount?>(account);
        }

        public Task<(IReadOnlyList<ClientReferentAccount> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId,
            Guid organizationId,
            bool includeInactive,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            IEnumerable<ClientReferentAccount> source = _store.Values
                .Where(a => a.ClientId == clientId && a.OrganizationId == organizationId);

            if (!includeInactive)
            {
                source = source.Where(a => a.IsActive);
            }

            var ordered = source.OrderByDescending(a => a.CreatedAtUtc).ToList();
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<ClientReferentAccount>, int)>((items, ordered.Count));
        }

        public Task<bool> ExistsByProfileUrlAsync(
            ClientId clientId,
            SocialNetworkId socialNetworkId,
            string profileUrl,
            ClientReferentAccountId? excludeId,
            CancellationToken ct)
        {
            var exists = _store.Values.Any(a =>
                a.ClientId == clientId &&
                a.SocialNetworkId == socialNetworkId &&
                a.ProfileUrl == profileUrl &&
                (excludeId is null || a.Id != excludeId.Value));
            return Task.FromResult(exists);
        }

        public Task AddAsync(ClientReferentAccount referentAccount, CancellationToken ct)
        {
            var key = (referentAccount.ClientId.Value, referentAccount.SocialNetworkId.Value, referentAccount.ProfileUrl);
            if (_uniqueIndex.Contains(key))
            {
                return Task.FromException(new InvalidOperationException("Duplicate (ClientId, SocialNetworkId, ProfileUrl)."));
            }
            _store[referentAccount.Id] = referentAccount;
            _uniqueIndex.Add(key);
            return Task.CompletedTask;
        }

        public void Update(ClientReferentAccount referentAccount)
        {
            // Remove old unique index entry
            foreach (var entry in _uniqueIndex.Where(k => k.ClientId == referentAccount.ClientId.Value && k.NetworkId == referentAccount.SocialNetworkId.Value).ToList())
            {
                _uniqueIndex.Remove(entry);
            }
            _store[referentAccount.Id] = referentAccount;
            _uniqueIndex.Add((referentAccount.ClientId.Value, referentAccount.SocialNetworkId.Value, referentAccount.ProfileUrl));
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    internal sealed class InMemorySocialClientRepository : ISocialClientRepository
    {
        private readonly Dictionary<(Guid ClientId, Guid OrgId), Client> _store = new();

        public void AddClient(Client client)
        {
            _store[(client.Id.Value, client.OrganizationId)] = client;
        }

        public void SetInactive(Guid clientId, Guid orgId)
        {
            if (_store.TryGetValue((clientId, orgId), out var client))
            {
                // Restore as inactive
                _store[(clientId, orgId)] = Client.Restore(
                    client.Id, client.OrganizationId, client.Name, client.Description,
                    false, client.CreatedAtUtc, client.UpdatedAtUtc);
            }
        }

        public Task<Client?> GetByIdAsync(ClientId id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue((id.Value, organizationId), out var client);
            return Task.FromResult(client);
        }

        public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(
            SocialClientQuery query, CancellationToken cancellationToken = default)
        {
            var items = _store.Values
                .Where(c => c.OrganizationId == query.OrganizationId)
                .ToList();
            return Task.FromResult<(IReadOnlyList<Client>, int)>((items, items.Count));
        }

        public Task AddAsync(Client client, CancellationToken cancellationToken = default)
        {
            _store[(client.Id.Value, client.OrganizationId)] = client;
            return Task.CompletedTask;
        }

        public void Update(Client client)
        {
            _store[(client.Id.Value, client.OrganizationId)] = client;
        }
    }

    internal sealed class InMemorySocialNetworkRepository : ISocialNetworkRepository
    {
        private readonly Dictionary<SocialNetworkId, SocialNetwork> _store = new();

        public void AddNetwork(SocialNetwork network)
        {
            _store[network.Id] = network;
        }

        public void SetInactive(SocialNetworkId id)
        {
            if (_store.TryGetValue(id, out var network))
            {
                _store[id] = SocialNetwork.Restore(
                    id, network.Code, network.DisplayName, network.IconKey,
                    false, network.CreatedAtUtc, network.UpdatedAtUtc);
            }
        }

        public Task<SocialNetwork?> GetByIdAsync(SocialNetworkId id, CancellationToken cancellationToken)
        {
            _store.TryGetValue(id, out var network);
            return Task.FromResult(network);
        }

        public Task<SocialNetwork?> GetByCodeAsync(string code, CancellationToken cancellationToken)
        {
            var network = _store.Values.FirstOrDefault(n => n.Code == code);
            return Task.FromResult(network);
        }

        public Task<PageResult<SocialNetwork>> ListAsync(SocialNetworkListQuery query, CancellationToken cancellationToken)
        {
            var items = _store.Values.ToList();
            return Task.FromResult(new PageResult<SocialNetwork>(items, 1, items.Count, items.Count));
        }

        public Task<IReadOnlyList<SocialNetwork>> GetByIdsAsync(
            IReadOnlyCollection<SocialNetworkId> ids, CancellationToken cancellationToken)
        {
            var result = ids
                .Where(id => _store.ContainsKey(id))
                .Select(id => _store[id])
                .ToList();
            return Task.FromResult<IReadOnlyList<SocialNetwork>>(result);
        }

        public Task AddAsync(SocialNetwork network, CancellationToken cancellationToken)
        {
            _store[network.Id] = network;
            return Task.CompletedTask;
        }

        public void Update(SocialNetwork network)
        {
            _store[network.Id] = network;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
