using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ClientBrandManuals;
using SocialStudio.Application.Features.Social.ClientBrandManuals.Activate;
using SocialStudio.Application.Features.Social.ClientBrandManuals.Create;
using SocialStudio.Application.Features.Social.ClientBrandManuals.Deactivate;
using SocialStudio.Application.Features.Social.ClientBrandManuals.Dtos;
using SocialStudio.Application.Features.Social.ClientBrandManuals.GetActive;
using SocialStudio.Application.Features.Social.ClientBrandManuals.GetById;
using SocialStudio.Application.Features.Social.ClientBrandManuals.List;
using SocialStudio.Application.Features.Social.ClientBrandManuals.Update;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Tests.Common;

namespace SocialStudio.Tests.Application.Social.ClientBrandManuals;

public sealed class ClientBrandManualLifecycleTests
{
    private static readonly DateTime Now = new(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 5, 18, 13, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<ColorEntryDto> ValidPalette =
        new[] { new ColorEntryDto("Primary", "#FF0000", null) };

    private static readonly IReadOnlyList<LogoEntryDto> EmptyLogos =
        Array.Empty<LogoEntryDto>();

    private static readonly IReadOnlyList<BackgroundEntryDto> EmptyBg =
        Array.Empty<BackgroundEntryDto>();

    private static readonly IReadOnlyList<TypographyEntryDto> EmptyTypo =
        Array.Empty<TypographyEntryDto>();

    // --- Create ---

    [Fact]
    public async Task CreateHandler_NoPriorActive_ReturnsCreatedResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        var result = await harness.Create.Handle(
            new CreateClientBrandManualCommand(
                orgId, clientId, "Test Manual",
                ValidPalette, EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Equal("Test Manual", result.Value.Title);
    }

    [Fact]
    public async Task CreateHandler_PriorActiveExists_DeactivatesPriorAndCreatesNew()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        // Create first manual
        var first = await harness.Create.Handle(
            new CreateClientBrandManualCommand(
                orgId, clientId, "First Manual",
                ValidPalette, EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        var firstId = first.Value.Id;

        // Create second manual — should deactivate first
        var second = await harness.Create.Handle(
            new CreateClientBrandManualCommand(
                orgId, clientId, "Second Manual",
                ValidPalette, EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.IsActive);

        // First manual should now be inactive
        var firstAfter = await harness.GetById.Handle(
            new GetClientBrandManualByIdQuery(orgId, clientId, firstId),
            CancellationToken.None);
        Assert.True(firstAfter.IsSuccess);
        Assert.False(firstAfter.Value.IsActive);
    }

    [Fact]
    public async Task CreateHandler_ClientNotFoundInOrg_ReturnsNotFound()
    {
        var harness = new Harness(Now);

        var result = await harness.Create.Handle(
            new CreateClientBrandManualCommand(
                Guid.NewGuid(), Guid.NewGuid(), "Manual",
                ValidPalette, EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_ClientInDifferentOrg_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, _) = harness.SeedActiveClient();

        var result = await harness.Create.Handle(
            new CreateClientBrandManualCommand(
                Guid.NewGuid(), clientId, "Manual",
                ValidPalette, EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    [Fact]
    public async Task CreateHandler_EmptyManual_ReturnsEmptyManualError()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        var result = await harness.Create.Handle(
            new CreateClientBrandManualCommand(
                orgId, clientId, "Manual",
                Array.Empty<ColorEntryDto>(), EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.empty_manual", result.Error.Code);
    }

    // --- GetById ---

    [Fact]
    public async Task GetByIdHandler_HappyPath_ReturnsResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");

        var result = await harness.GetById.Handle(
            new GetClientBrandManualByIdQuery(orgId, clientId, created.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetByIdHandler_WrongClient_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");

        // Use a different clientId
        var (clientId2, _) = harness.SeedActiveClient(orgId);

        var result = await harness.GetById.Handle(
            new GetClientBrandManualByIdQuery(orgId, clientId2, created.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetByIdHandler_WrongOrg_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");

        var result = await harness.GetById.Handle(
            new GetClientBrandManualByIdQuery(Guid.NewGuid(), clientId, created.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetByIdHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        var result = await harness.GetById.Handle(
            new GetClientBrandManualByIdQuery(orgId, clientId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    // --- GetActive ---

    [Fact]
    public async Task GetActiveHandler_NoActiveManual_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        var result = await harness.GetActive.Handle(
            new GetActiveClientBrandManualQuery(orgId, clientId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetActiveHandler_ActiveExists_ReturnsResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Active Manual");

        var result = await harness.GetActive.Handle(
            new GetActiveClientBrandManualQuery(orgId, clientId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.Id, result.Value.Id);
        Assert.True(result.Value.IsActive);
    }

    // --- List ---

    [Fact]
    public async Task ListHandler_IncludeInactiveFalse_ReturnsOnlyActive()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var m1 = await CreateManual(harness, orgId, clientId, "Manual 1");
        await CreateManual(harness, orgId, clientId, "Manual 2"); // creates new active, m1 becomes inactive

        var result = await harness.List.Handle(
            new ListClientBrandManualsByClientQuery(orgId, clientId, false, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.True(result.Value.Items[0].IsActive);
    }

    [Fact]
    public async Task ListHandler_IncludeInactiveTrue_ReturnsAll()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        await CreateManual(harness, orgId, clientId, "Manual 1");
        await CreateManual(harness, orgId, clientId, "Manual 2");

        var result = await harness.List.Handle(
            new ListClientBrandManualsByClientQuery(orgId, clientId, true, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task ListHandler_OrderedByCreatedAtUtcDesc()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        harness.Clock.UtcNow = Now;
        await CreateManual(harness, orgId, clientId, "First");
        harness.Clock.UtcNow = Later;
        await CreateManual(harness, orgId, clientId, "Second");

        var result = await harness.List.Handle(
            new ListClientBrandManualsByClientQuery(orgId, clientId, true, 1, 20),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        // Second (Later) should be first
        Assert.Equal("Second", result.Value.Items[0].Title);
        Assert.Equal("First", result.Value.Items[1].Title);
    }

    // --- Update ---

    [Fact]
    public async Task UpdateHandler_HappyPath_ReturnsUpdatedResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Original");

        var result = await harness.Update.Handle(
            new UpdateClientBrandManualCommand(
                orgId, clientId, created.Id, "Updated Title",
                ValidPalette, EmptyLogos, EmptyBg, EmptyTypo,
                "New voice", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Title", result.Value.Title);
        Assert.Equal("New voice", result.Value.VoiceAndTone);
    }

    [Fact]
    public async Task UpdateHandler_ClientIdMismatch_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");

        var (clientId2, _) = harness.SeedActiveClient(orgId);

        var result = await harness.Update.Handle(
            new UpdateClientBrandManualCommand(
                orgId, clientId2, created.Id, "Updated",
                ValidPalette, EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    [Fact]
    public async Task UpdateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        var result = await harness.Update.Handle(
            new UpdateClientBrandManualCommand(
                orgId, clientId, Guid.NewGuid(), "Updated",
                ValidPalette, EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    [Fact]
    public async Task UpdateHandler_EmptyManual_ReturnsEmptyManualError()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");

        var result = await harness.Update.Handle(
            new UpdateClientBrandManualCommand(
                orgId, clientId, created.Id, "Updated",
                Array.Empty<ColorEntryDto>(), EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.empty_manual", result.Error.Code);
    }

    // --- Deactivate ---

    [Fact]
    public async Task DeactivateHandler_ActiveManual_ReturnsDeactivatedResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");

        var result = await harness.Deactivate.Handle(
            new DeactivateClientBrandManualCommand(orgId, clientId, created.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task DeactivateHandler_AlreadyInactive_ReturnsAlreadyInactive()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");
        await harness.Deactivate.Handle(
            new DeactivateClientBrandManualCommand(orgId, clientId, created.Id),
            CancellationToken.None);

        var result = await harness.Deactivate.Handle(
            new DeactivateClientBrandManualCommand(orgId, clientId, created.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.already_inactive", result.Error.Code);
    }

    [Fact]
    public async Task DeactivateHandler_WrongOrg_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");

        var result = await harness.Deactivate.Handle(
            new DeactivateClientBrandManualCommand(Guid.NewGuid(), clientId, created.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    [Fact]
    public async Task DeactivateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        var result = await harness.Deactivate.Handle(
            new DeactivateClientBrandManualCommand(orgId, clientId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    // --- Activate ---

    [Fact]
    public async Task ActivateHandler_NoOtherActive_ReturnsActivatedResponse()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");
        await harness.Deactivate.Handle(
            new DeactivateClientBrandManualCommand(orgId, clientId, created.Id),
            CancellationToken.None);

        var result = await harness.Activate.Handle(
            new ActivateClientBrandManualCommand(orgId, clientId, created.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task ActivateHandler_AnotherManualAlreadyActive_ReturnsAnotherManualActive()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        // Create M1, then create M2 (M1 auto-deactivated)
        var m1 = await CreateManual(harness, orgId, clientId, "Manual 1");
        await CreateManual(harness, orgId, clientId, "Manual 2");

        // Try to activate M1 while M2 is active
        var result = await harness.Activate.Handle(
            new ActivateClientBrandManualCommand(orgId, clientId, m1.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.another_active", result.Error.Code);
    }

    [Fact]
    public async Task ActivateHandler_AlreadyActive_ReturnsAlreadyActive()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();
        var created = await CreateManual(harness, orgId, clientId, "Manual");

        var result = await harness.Activate.Handle(
            new ActivateClientBrandManualCommand(orgId, clientId, created.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.already_active", result.Error.Code);
    }

    [Fact]
    public async Task ActivateHandler_UnknownId_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var (clientId, orgId) = harness.SeedActiveClient();

        var result = await harness.Activate.Handle(
            new ActivateClientBrandManualCommand(orgId, clientId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.client_brand_manuals.not_found", result.Error.Code);
    }

    // --- Helpers ---

    private static async Task<ClientBrandManualResponse> CreateManual(
        Harness harness, Guid orgId, Guid clientId, string title)
    {
        var result = await harness.Create.Handle(
            new CreateClientBrandManualCommand(
                orgId, clientId, title,
                ValidPalette, EmptyLogos, EmptyBg, EmptyTypo,
                null, null),
            CancellationToken.None);
        Assert.True(result.IsSuccess, $"CreateManual failed: {result.Error.Code}");
        return result.Value;
    }

    // --- Harness ---

    private sealed class Harness
    {
        private readonly InMemoryClientBrandManualRepository _manualRepo;
        private readonly InMemorySocialClientRepository _clientRepo;

        public Harness(DateTime nowUtc)
        {
            Clock = new TestClock(nowUtc);
            _clientRepo = new InMemorySocialClientRepository();
            _manualRepo = new InMemoryClientBrandManualRepository();

            Create = new CreateClientBrandManualHandler(_manualRepo, _clientRepo, Clock);
            GetById = new GetClientBrandManualByIdHandler(_manualRepo, _clientRepo);
            GetActive = new GetActiveClientBrandManualHandler(_manualRepo, _clientRepo);
            List = new ListClientBrandManualsByClientHandler(_manualRepo, _clientRepo);
            Update = new UpdateClientBrandManualHandler(_manualRepo, _clientRepo, Clock);
            Deactivate = new DeactivateClientBrandManualHandler(_manualRepo, Clock);
            Activate = new ActivateClientBrandManualHandler(_manualRepo, Clock);
        }

        public TestClock Clock { get; }

        public CreateClientBrandManualHandler Create { get; }
        public GetClientBrandManualByIdHandler GetById { get; }
        public GetActiveClientBrandManualHandler GetActive { get; }
        public ListClientBrandManualsByClientHandler List { get; }
        public UpdateClientBrandManualHandler Update { get; }
        public DeactivateClientBrandManualHandler Deactivate { get; }
        public ActivateClientBrandManualHandler Activate { get; }

        /// <summary>Seeds an active client in the given org (new org if null).</summary>
        public (Guid ClientId, Guid OrgId) SeedActiveClient(Guid? orgId = null)
        {
            var org = orgId ?? Guid.NewGuid();
            var clientId = ClientId.New();
            _clientRepo.AddClient(Client.Restore(
                clientId, org, "Test Client", null, true,
                Clock.UtcNow, Clock.UtcNow));
            return (clientId.Value, org);
        }
    }

    // --- InMemory repositories ---

    internal sealed class InMemoryClientBrandManualRepository : IClientBrandManualRepository
    {
        private readonly Dictionary<ClientBrandManualId, ClientBrandManual> _store = new();

        public Task<ClientBrandManual?> GetByIdAsync(
            ClientBrandManualId id,
            Guid organizationId,
            CancellationToken ct)
        {
            _store.TryGetValue(id, out var manual);
            if (manual is null || manual.OrganizationId != organizationId)
            {
                return Task.FromResult<ClientBrandManual?>(null);
            }
            return Task.FromResult<ClientBrandManual?>(manual);
        }

        public Task<ClientBrandManual?> GetActiveByClientAsync(
            ClientId clientId,
            Guid organizationId,
            CancellationToken ct)
        {
            var manual = _store.Values.FirstOrDefault(
                m => m.ClientId == clientId && m.OrganizationId == organizationId && m.IsActive);
            return Task.FromResult(manual);
        }

        public Task<(IReadOnlyList<ClientBrandManual> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId,
            Guid organizationId,
            bool includeInactive,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            IEnumerable<ClientBrandManual> source = _store.Values
                .Where(m => m.ClientId == clientId && m.OrganizationId == organizationId);

            if (!includeInactive)
            {
                source = source.Where(m => m.IsActive);
            }

            var ordered = source.OrderByDescending(m => m.CreatedAtUtc).ToList();
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<ClientBrandManual>, int)>((items, ordered.Count));
        }

        public Task AddAsync(ClientBrandManual manual, CancellationToken ct)
        {
            _store[manual.Id] = manual;
            return Task.CompletedTask;
        }

        public void Update(ClientBrandManual manual)
        {
            _store[manual.Id] = manual;
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

        public Task<Client?> GetByIdAsync(
            ClientId id,
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            _store.TryGetValue((id.Value, organizationId), out var client);
            return Task.FromResult(client);
        }

        public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(
            SocialClientQuery query,
            CancellationToken cancellationToken = default)
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
}
