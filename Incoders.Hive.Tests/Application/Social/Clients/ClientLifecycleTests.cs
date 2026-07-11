using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.Clients.Activate;
using Incoders.Hive.Application.Features.Social.Clients.Create;
using Incoders.Hive.Application.Features.Social.Clients.Deactivate;
using Incoders.Hive.Application.Features.Social.Clients.GetAll;
using Incoders.Hive.Application.Features.Social.Clients.GetById;
using Incoders.Hive.Application.Features.Social.Clients.Update;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Social.Clients;

public sealed class ClientLifecycleTests
{
    private static readonly DateTime Now = new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_ThenGetById_ReturnsClientForOrganization()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();

        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme Social", "Primary client"),
            CancellationToken.None);

        Assert.True(created.IsSuccess);

        var fetched = await harness.GetById.Handle(
            new GetSocialClientByIdQuery(created.Value.Id, organizationId),
            CancellationToken.None);

        Assert.True(fetched.IsSuccess);
        Assert.Equal("Acme Social", fetched.Value.Name);
        Assert.Equal(organizationId, fetched.Value.OrganizationId);
    }

    [Fact]
    public async Task GetById_DoesNotCrossOrganizationBoundary()
    {
        var harness = new SocialClientHarness(Now);
        var ownerOrganizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();

        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(ownerOrganizationId, "Acme Social", null),
            CancellationToken.None);

        var fetched = await harness.GetById.Handle(
            new GetSocialClientByIdQuery(created.Value.Id, otherOrganizationId),
            CancellationToken.None);

        Assert.True(fetched.IsFailure);
        Assert.Equal("social.clients.not_found", fetched.Error.Code);
    }

    [Fact]
    public async Task List_ReturnsOnlyOrganizationClients()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();

        await harness.Create.Handle(new CreateSocialClientCommand(organizationId, "Acme", null), CancellationToken.None);
        await harness.Create.Handle(new CreateSocialClientCommand(Guid.NewGuid(), "Other", null), CancellationToken.None);

        var result = await harness.List.Handle(
            new GetSocialClientsQuery(organizationId, null, false, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("Acme", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task List_ReturnsAllOrganizationClients_WhenOrganizationFilterIsNull()
    {
        var harness = new SocialClientHarness(Now);

        await harness.Create.Handle(new CreateSocialClientCommand(Guid.NewGuid(), "Acme", null), CancellationToken.None);
        await harness.Create.Handle(new CreateSocialClientCommand(Guid.NewGuid(), "Other", null), CancellationToken.None);

        var result = await harness.List.Handle(
            new GetSocialClientsQuery(null, null, false, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
    }

    // --- Update ---

    [Fact]
    public async Task Update_RenamesClient_WhenWithinOrganization()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme", "Old description"),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateSocialClientCommand(created.Value.Id, organizationId, "Acme Renamed", "New description"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme Renamed", result.Value.Name);
        Assert.Equal("New description", result.Value.Description);
    }

    [Fact]
    public async Task Update_UpdatesDescription_WhenProvided()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme", null),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateSocialClientCommand(created.Value.Id, organizationId, "Acme", "Added description"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Added description", result.Value.Description);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();

        var result = await harness.Update.Handle(
            new UpdateSocialClientCommand(Guid.NewGuid(), organizationId, "Ghost", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenCrossingOrganizationBoundary()
    {
        var harness = new SocialClientHarness(Now);
        var ownerOrgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(ownerOrgId, "Acme", null),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateSocialClientCommand(created.Value.Id, otherOrgId, "Hacked", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Update_ReturnsError_WhenNameEmpty()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme", null),
            CancellationToken.None);

        var result = await harness.Update.Handle(
            new UpdateSocialClientCommand(created.Value.Id, organizationId, "   ", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.name_required", result.Error.Code);
    }

    // --- Deactivate ---

    [Fact]
    public async Task Deactivate_MarksClientInactive_WhenWithinOrganization()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme", null),
            CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateSocialClientCommand(created.Value.Id, organizationId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task Deactivate_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();

        var result = await harness.Deactivate.Handle(
            new DeactivateSocialClientCommand(Guid.NewGuid(), organizationId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Deactivate_ReturnsNotFound_WhenCrossingOrganizationBoundary()
    {
        var harness = new SocialClientHarness(Now);
        var ownerOrgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(ownerOrgId, "Acme", null),
            CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateSocialClientCommand(created.Value.Id, otherOrgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Deactivate_ReturnsAlreadyInactive_WhenInvokedTwice()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme", null),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateSocialClientCommand(created.Value.Id, organizationId),
            CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateSocialClientCommand(created.Value.Id, organizationId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.already_inactive", result.Error.Code);
    }

    // --- Activate ---

    [Fact]
    public async Task Activate_MarksClientActive_WhenInvokedOnInactive()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme", null),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateSocialClientCommand(created.Value.Id, organizationId),
            CancellationToken.None);

        var result = await harness.Activate.Handle(
            new ActivateSocialClientCommand(created.Value.Id, organizationId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Activate_ReturnsNotFound_WhenIdDoesNotExist()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();

        var result = await harness.Activate.Handle(
            new ActivateSocialClientCommand(Guid.NewGuid(), organizationId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Activate_ReturnsAlreadyActive_WhenAlreadyActive()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme", null),
            CancellationToken.None);

        var result = await harness.Activate.Handle(
            new ActivateSocialClientCommand(created.Value.Id, organizationId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.already_active", result.Error.Code);
    }

    // --- Issue #70: SignsOwnSlides opt-in (plain boolean, client-field-only) ---

    [Fact]
    public async Task Create_WithSignsOwnSlidesTrue_PersistsFlag()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();

        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme Social", null, SignsOwnSlides: true),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.True(created.Value.SignsOwnSlides);

        var fetched = await harness.GetById.Handle(
            new GetSocialClientByIdQuery(created.Value.Id, organizationId),
            CancellationToken.None);

        Assert.True(fetched.Value.SignsOwnSlides);
    }

    [Fact]
    public async Task Create_WithoutSignsOwnSlides_DefaultsToFalse()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();

        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme Social", null),
            CancellationToken.None);

        Assert.False(created.Value.SignsOwnSlides);
    }

    [Fact]
    public async Task Update_CanToggleSignsOwnSlides_TrueThenFalse()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme Social", null),
            CancellationToken.None);

        var enabled = await harness.Update.Handle(
            new UpdateSocialClientCommand(created.Value.Id, organizationId, "Acme Social", null, SignsOwnSlides: true),
            CancellationToken.None);
        Assert.True(enabled.Value.SignsOwnSlides);

        var disabled = await harness.Update.Handle(
            new UpdateSocialClientCommand(created.Value.Id, organizationId, "Acme Social", null, SignsOwnSlides: false),
            CancellationToken.None);
        Assert.False(disabled.Value.SignsOwnSlides);
    }

    // --- List after lifecycle ---

    [Fact]
    public async Task List_ExcludesInactive_ByDefault_AfterDeactivate()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme", null),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateSocialClientCommand(created.Value.Id, organizationId),
            CancellationToken.None);

        var result = await harness.List.Handle(
            new GetSocialClientsQuery(organizationId, null, false, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task List_IncludesInactive_WhenIncludeInactiveTrue_AfterDeactivate()
    {
        var harness = new SocialClientHarness(Now);
        var organizationId = Guid.NewGuid();
        var created = await harness.Create.Handle(
            new CreateSocialClientCommand(organizationId, "Acme", null),
            CancellationToken.None);
        await harness.Deactivate.Handle(
            new DeactivateSocialClientCommand(created.Value.Id, organizationId),
            CancellationToken.None);

        var result = await harness.List.Handle(
            new GetSocialClientsQuery(organizationId, null, true, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.False(result.Value.Items[0].IsActive);
    }

    private sealed class SocialClientHarness
    {
        public SocialClientHarness(DateTime nowUtc)
        {
            var repository = new InMemorySocialClientRepository();
            var unitOfWork = new TestUnitOfWork();
            var clock = new TestClock(nowUtc);

            Create = new CreateSocialClientHandler(repository, unitOfWork, clock);
            GetById = new GetSocialClientByIdHandler(repository);
            List = new GetSocialClientsHandler(repository);
            Update = new UpdateSocialClientHandler(repository, unitOfWork, clock);
            Deactivate = new DeactivateSocialClientHandler(repository, unitOfWork, clock);
            Activate = new ActivateSocialClientHandler(repository, unitOfWork, clock);
        }

        public CreateSocialClientHandler Create { get; }
        public GetSocialClientByIdHandler GetById { get; }
        public GetSocialClientsHandler List { get; }
        public UpdateSocialClientHandler Update { get; }
        public DeactivateSocialClientHandler Deactivate { get; }
        public ActivateSocialClientHandler Activate { get; }
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class InMemorySocialClientRepository : ISocialClientRepository
    {
        private readonly Dictionary<ClientId, Client> _clients = new();

        public Task<Client?> GetByIdAsync(
            ClientId id,
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            _clients.TryGetValue(id, out var client);
            return Task.FromResult(client?.OrganizationId == organizationId ? client : null);
        }

        public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(
            SocialClientQuery query,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Client> source = _clients.Values;

            if (query.OrganizationId.HasValue)
            {
                source = source.Where(x => x.OrganizationId == query.OrganizationId.Value);
            }

            if (!query.IncludeInactive)
            {
                source = source.Where(x => x.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(query.NameContains))
            {
                source = source.Where(x => x.Name.Contains(query.NameContains, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = source.OrderBy(x => x.Name).ToList();
            var items = ordered
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            return Task.FromResult(((IReadOnlyList<Client>)items, ordered.Count));
        }

        public Task AddAsync(Client client, CancellationToken cancellationToken = default)
        {
            _clients.Add(client.Id, client);
            return Task.CompletedTask;
        }

        public void Update(Client client)
        {
            _clients[client.Id] = client;
        }
    }
}
