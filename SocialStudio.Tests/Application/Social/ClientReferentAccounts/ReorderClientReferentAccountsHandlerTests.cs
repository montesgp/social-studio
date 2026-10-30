using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ClientReferentAccounts.Reorder;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientReferentAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using SocialStudio.Tests.Common;

namespace SocialStudio.Tests.Application.Social.ClientReferentAccounts;

/// <summary>
/// TDD tests for ReorderClientReferentAccountsCommandHandler.
/// Written BEFORE the handler implementation (RED phase per strict TDD).
/// </summary>
public sealed class ReorderClientReferentAccountsHandlerTests
{
    private static readonly DateTime Now = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);

    // --- Handle_ValidOrder_AssignsZeroBasedSortOrder ---

    [Fact]
    public async Task Handle_ValidOrder_AssignsZeroBasedSortOrder()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var (accA, accB, accC) = harness.SeedThreeAccounts(clientId, orgId);

        // Reorder: C first, then A, then B
        var command = new ReorderClientReferentAccountsCommand(
            clientId, orgId, new[] { accC, accA, accB });

        var result = await harness.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The returned list should have sort_order: C=0, A=1, B=2
        var ordered = result.Value.OrderBy(x => x.SortOrder).ToList();
        Assert.Equal(accC, ordered[0].Id);
        Assert.Equal(0, ordered[0].SortOrder);
        Assert.Equal(accA, ordered[1].Id);
        Assert.Equal(1, ordered[1].SortOrder);
        Assert.Equal(accB, ordered[2].Id);
        Assert.Equal(2, ordered[2].SortOrder);
    }

    // --- Handle_MismatchedIds_ReturnsValidationError ---

    [Fact]
    public async Task Handle_MismatchedIds_ReturnsValidationError()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var (accA, accB, _) = harness.SeedThreeAccounts(clientId, orgId);

        // Only supply 2 of 3 IDs — mismatch
        var command = new ReorderClientReferentAccountsCommand(
            clientId, orgId, new[] { accA, accB });

        var result = await harness.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Handle_IdempotentOrder_SameOrderTwice_Succeeds ---

    [Fact]
    public async Task Handle_IdempotentOrder_SameOrderTwice_Succeeds()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var (accA, accB, accC) = harness.SeedThreeAccounts(clientId, orgId);

        var command = new ReorderClientReferentAccountsCommand(
            clientId, orgId, new[] { accC, accA, accB });

        // First call
        var result1 = await harness.Handler.Handle(command, CancellationToken.None);
        Assert.True(result1.IsSuccess);

        // Second call with same order — must succeed and be idempotent
        var result2 = await harness.Handler.Handle(command, CancellationToken.None);
        Assert.True(result2.IsSuccess);

        var ordered = result2.Value.OrderBy(x => x.SortOrder).ToList();
        Assert.Equal(accC, ordered[0].Id);
        Assert.Equal(0, ordered[0].SortOrder);
    }

    // --- Handle_EmptyIds_ReturnsValidationError ---

    [Fact]
    public async Task Handle_EmptyIds_ReturnsValidationError()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        harness.SeedThreeAccounts(clientId, orgId);

        var command = new ReorderClientReferentAccountsCommand(
            clientId, orgId, Array.Empty<Guid>());

        var result = await harness.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // --- Harness ---

    private sealed class Harness
    {
        private readonly InMemoryClientReferentAccountRepoForReorder _accountRepo;
        private readonly InMemorySocialClientRepoForReorder _clientRepo;

        public Harness(DateTime nowUtc)
        {
            Clock = new TestClock(nowUtc);
            _clientRepo = new InMemorySocialClientRepoForReorder();
            _accountRepo = new InMemoryClientReferentAccountRepoForReorder();
            Handler = new ReorderClientReferentAccountsCommandHandler(_accountRepo, _clientRepo, Clock);
        }

        public TestClock Clock { get; }
        public ReorderClientReferentAccountsCommandHandler Handler { get; }

        public (Guid ClientId, Guid OrgId) SeedActiveClient(Guid? orgId = null)
        {
            var org = orgId ?? Guid.NewGuid();
            var clientId = ClientId.New();
            _clientRepo.AddClient(Client.Restore(
                clientId, org, "Test Client", null, true,
                Clock.UtcNow, Clock.UtcNow));
            return (clientId.Value, org);
        }

        /// <summary>Seeds 3 accounts for the client and returns their IDs in creation order.</summary>
        public (Guid A, Guid B, Guid C) SeedThreeAccounts(Guid clientId, Guid orgId)
        {
            var networkId = SocialNetworkId.New();
            var idA = ClientReferentAccountId.New();
            var idB = ClientReferentAccountId.New();
            var idC = ClientReferentAccountId.New();
            var cid = new ClientId(clientId);

            _accountRepo.AddAccount(ClientReferentAccount.Restore(
                idA, cid, orgId, networkId, "https://instagram.com/a", null, null, null,
                true, Clock.UtcNow, Clock.UtcNow, 0));

            _accountRepo.AddAccount(ClientReferentAccount.Restore(
                idB, cid, orgId, networkId, "https://instagram.com/b", null, null, null,
                true, Clock.UtcNow, Clock.UtcNow, 1));

            _accountRepo.AddAccount(ClientReferentAccount.Restore(
                idC, cid, orgId, networkId, "https://instagram.com/c", null, null, null,
                true, Clock.UtcNow, Clock.UtcNow, 2));

            return (idA.Value, idB.Value, idC.Value);
        }
    }

    // --- InMemory repositories ---

    internal sealed class InMemoryClientReferentAccountRepoForReorder : IClientReferentAccountRepository
    {
        private readonly Dictionary<Guid, ClientReferentAccount> _store = new();

        public void AddAccount(ClientReferentAccount account)
        {
            _store[account.Id.Value] = account;
        }

        public Task<ClientReferentAccount?> GetByIdAsync(
            ClientReferentAccountId id, Guid organizationId, CancellationToken ct)
        {
            _store.TryGetValue(id.Value, out var account);
            return Task.FromResult(account?.OrganizationId == organizationId ? account : null);
        }

        public Task<(IReadOnlyList<ClientReferentAccount> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId, Guid organizationId, bool includeInactive, int page, int pageSize,
            CancellationToken ct)
        {
            var items = _store.Values
                .Where(a => a.ClientId == clientId && a.OrganizationId == organizationId)
                .OrderBy(a => a.SortOrder).ThenBy(a => a.CreatedAtUtc)
                .Skip((page - 1) * pageSize).Take(pageSize).ToList();
            var total = _store.Values.Count(a => a.ClientId == clientId && a.OrganizationId == organizationId);
            return Task.FromResult<(IReadOnlyList<ClientReferentAccount>, int)>((items, total));
        }

        public Task<IReadOnlyList<ClientReferentAccount>> GetAllByClientAsync(
            ClientId clientId, Guid organizationId, CancellationToken ct)
        {
            IReadOnlyList<ClientReferentAccount> items = _store.Values
                .Where(a => a.ClientId == clientId && a.OrganizationId == organizationId)
                .ToList();
            return Task.FromResult(items);
        }

        public Task<bool> ExistsByProfileUrlAsync(
            ClientId clientId, SocialNetworkId socialNetworkId, string profileUrl,
            ClientReferentAccountId? excludeId, CancellationToken ct)
        {
            return Task.FromResult(false);
        }

        public Task AddAsync(ClientReferentAccount referentAccount, CancellationToken ct)
        {
            _store[referentAccount.Id.Value] = referentAccount;
            return Task.CompletedTask;
        }

        public void Update(ClientReferentAccount account)
        {
            _store[account.Id.Value] = account;
        }

        public Task ReorderAsync(
            ClientId clientId, Guid organizationId, IReadOnlyList<Guid> orderedIds,
            CancellationToken ct)
        {
            var accounts = _store.Values
                .Where(a => a.ClientId == clientId && a.OrganizationId == organizationId)
                .ToList();

            var reorderResult = ClientReferentAccount.Reorder(accounts, orderedIds, DateTime.UtcNow);
            if (reorderResult.IsSuccess)
            {
                foreach (var account in accounts)
                {
                    _store[account.Id.Value] = account;
                }
            }
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    internal sealed class InMemorySocialClientRepoForReorder : ISocialClientRepository
    {
        private readonly Dictionary<(Guid ClientId, Guid OrgId), Client> _store = new();

        public void AddClient(Client client)
        {
            _store[(client.Id.Value, client.OrganizationId)] = client;
        }

        public Task<Client?> GetByIdAsync(
            ClientId id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue((id.Value, organizationId), out var client);
            return Task.FromResult(client);
        }

        public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(
            SocialClientQuery query, CancellationToken cancellationToken = default)
        {
            var items = _store.Values.Where(c => c.OrganizationId == query.OrganizationId).ToList();
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
}
