using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.Clients.Niches.Assign;
using SocialStudio.Application.Features.Social.Clients.Niches.Get;
using SocialStudio.Application.Features.Social.Niches.Create;
using SocialStudio.Application.Features.Social.Niches.Deactivate;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Tests.Common;

namespace SocialStudio.Tests.Application.Social.Niches;

/// <summary>
/// A client picks niches from the sysadmin-managed catalog. Inventing one inline is not possible:
/// an unknown or deactivated niche id is rejected, never created on the fly.
/// </summary>
public sealed class ClientNicheAssignmentTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientGuid = Guid.NewGuid();

    [Fact]
    public async Task Assign_SeveralNiches_AssociatesThemAll()
    {
        var harness = new Harness();
        var software = await harness.SeedNiche("software", "Software");
        var technology = await harness.SeedNiche("technology", "Technology");
        var ai = await harness.SeedNiche("ai", "Artificial Intelligence");

        var result = await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, [software, technology, ai]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { "Artificial Intelligence", "Software", "Technology" },
            result.Value.Niches.Select(x => x.DisplayName).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Assign_ReplacesThePreviousSelection()
    {
        var harness = new Harness();
        var software = await harness.SeedNiche("software", "Software");
        var gastronomy = await harness.SeedNiche("gastronomy", "Gastronomy");

        await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, [software]),
            CancellationToken.None);

        var result = await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, [gastronomy]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Niches);
        Assert.Equal("gastronomy", result.Value.Niches[0].Code);
    }

    [Fact]
    public async Task Assign_EmptySelection_RemovesEveryAssociation()
    {
        var harness = new Harness();
        var software = await harness.SeedNiche("software", "Software");
        await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, [software]),
            CancellationToken.None);

        var result = await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, []),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Niches);
    }

    [Fact]
    public async Task Assign_DeduplicatesRepeatedIds()
    {
        var harness = new Harness();
        var software = await harness.SeedNiche("software", "Software");

        var result = await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, [software, software]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Niches);
    }

    [Fact]
    public async Task Assign_UnknownNiche_IsRejected()
    {
        var harness = new Harness();

        var result = await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Assign_InactiveNiche_IsRejected()
    {
        var harness = new Harness();
        var software = await harness.SeedNiche("software", "Software");
        await harness.Deactivate.Handle(new DeactivateNicheCommand(software), CancellationToken.None);

        var result = await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, [software]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.niches.inactive", result.Error.Code);
    }

    [Fact]
    public async Task Assign_UnknownClient_ReturnsClientNotFound()
    {
        var harness = new Harness(clientExists: false);
        var software = await harness.SeedNiche("software", "Software");

        var result = await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, [software]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Get_ReturnsTheCurrentSelectionOrderedByDisplayName()
    {
        var harness = new Harness();
        var software = await harness.SeedNiche("software", "Software");
        var ai = await harness.SeedNiche("ai", "Artificial Intelligence");
        await harness.Assign.Handle(
            new AssignClientNichesCommand(ClientGuid, OrgId, [software, ai]),
            CancellationToken.None);

        var result = await harness.Get.Handle(
            new GetClientNichesQuery(ClientGuid, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { "Artificial Intelligence", "Software" },
            result.Value.Niches.Select(x => x.DisplayName).ToArray());
    }

    [Fact]
    public async Task Get_UnknownClient_ReturnsClientNotFound()
    {
        var harness = new Harness(clientExists: false);

        var result = await harness.Get.Handle(
            new GetClientNichesQuery(ClientGuid, OrgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClientErrors.NotFound.Code, result.Error.Code);
    }

    private sealed class Harness
    {
        private readonly CreateNicheHandler _create;

        public Harness(bool clientExists = true)
        {
            var niches = new InMemoryNicheRepository();
            var links = new InMemoryClientNicheRepository();
            var clients = new FakeClientRepository(clientExists);
            var clock = new TestClock(Now);

            _create = new CreateNicheHandler(niches, clock);
            Deactivate = new DeactivateNicheHandler(niches, clock);
            Assign = new AssignClientNichesHandler(clients, clients, niches, links, clock);
            Get = new GetClientNichesHandler(clients, clients, niches, links);
        }

        public DeactivateNicheHandler Deactivate { get; }
        public AssignClientNichesHandler Assign { get; }
        public GetClientNichesHandler Get { get; }

        public async Task<Guid> SeedNiche(string code, string displayName)
        {
            var result = await _create.Handle(
                new CreateNicheCommand(code, displayName, null, []),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
            return result.Value.Id;
        }
    }

    private sealed class FakeClientRepository : ISocialClientRepository, ISocialClientDirectoryRepository
    {
        private readonly bool _exists;

        public FakeClientRepository(bool exists) => _exists = exists;

        public Task<Client?> GetByIdAsync(ClientId id, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_exists ? Restore(id, organizationId) : null);

        public Task<Client?> GetByIdAcrossOrganizationsAsync(ClientId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_exists ? Restore(id, OrgId) : null);

        private static Client? Restore(ClientId id, Guid organizationId) =>
            Client.Restore(id, organizationId, "Test Client", null, true, Now, Now, null, null, null);

        public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(SocialClientQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<Client>, int)>(([], 0));

        public Task AddAsync(Client client, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(Client client) { }
    }
}
