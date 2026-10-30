using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Tenants;
using SocialStudio.Infrastructure.Integrations;
using SocialStudio.Tests.Application.Integrations;
using SocialStudio.Tests.Common;

namespace SocialStudio.Tests.Infrastructure.Integrations;

/// <summary>
/// WU-6 RED tests (task 6.4): <c>ComfyUiOrganizationSeeder</c> — auto-provisions the
/// <c>comfyui</c> integration for every ACTIVE organization, idempotently, mirroring
/// <see cref="ClaudePromptCliOrganizationSeeder"/>. Refs: CUI-INT-02-a, CUI-INT-02-b; design §2.2.
/// </summary>
public sealed class ComfyUiOrganizationSeederTests
{
    private static readonly DateTime Now = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Seeds_ConnectedComfyUiIntegration_ForEachActiveOrganization()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        await new IntegrationCatalogSeeder(providers, new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var tenantA = NewTenant("Org A");
        var tenantB = NewTenant("Org B");
        var tenants = new InMemoryTenantRepository(tenantA, tenantB);
        var integrations = new InMemoryOrganizationIntegrationRepository();

        var seeder = new ComfyUiOrganizationSeeder(providers, integrations, tenants, new NoopUnitOfWork(), new TestClock(Now));
        await seeder.SeedAsync();

        var provider = await providers.GetByKeyAsync("comfyui");
        Assert.NotNull(provider);
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            var integration = await integrations.GetByOrganizationAndProviderAsync(tenant.Id.Value, provider!.Id);
            Assert.NotNull(integration);
        }
    }

    [Fact]
    public async Task SkipsInactiveOrganizations()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        await new IntegrationCatalogSeeder(providers, new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var inactive = NewTenant("Inactive", active: false);
        var tenants = new InMemoryTenantRepository(inactive);
        var integrations = new InMemoryOrganizationIntegrationRepository();

        await new ComfyUiOrganizationSeeder(providers, integrations, tenants, new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var provider = await providers.GetByKeyAsync("comfyui");
        Assert.Null(await integrations.GetByOrganizationAndProviderAsync(inactive.Id.Value, provider!.Id));
    }

    [Fact]
    public async Task IsIdempotent_DoesNotDuplicateExisting()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        await new IntegrationCatalogSeeder(providers, new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var tenant = NewTenant("Org A");
        var tenants = new InMemoryTenantRepository(tenant);
        var integrations = new InMemoryOrganizationIntegrationRepository();
        var seeder = new ComfyUiOrganizationSeeder(providers, integrations, tenants, new NoopUnitOfWork(), new TestClock(Now));

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var provider = await providers.GetByKeyAsync("comfyui");
        Assert.Equal(1, integrations.CountFor(tenant.Id.Value, provider!.Id));
    }

    [Fact]
    public async Task OnlySeedsComfyUi_NotOtherProviders()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        await new IntegrationCatalogSeeder(providers, new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var tenant = NewTenant("Org A");
        var integrations = new InMemoryOrganizationIntegrationRepository();

        await new ComfyUiOrganizationSeeder(providers, integrations, new InMemoryTenantRepository(tenant), new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var openai = await providers.GetByKeyAsync("openai");
        Assert.Null(await integrations.GetByOrganizationAndProviderAsync(tenant.Id.Value, openai!.Id));
        Assert.Equal(1, integrations.Count);
    }

    private static Tenant NewTenant(string name, bool active = true)
    {
        var tenant = Tenant.Create(new TenantId(Guid.NewGuid()), name, Guid.NewGuid(), Now).Value;
        if (!active)
        {
            tenant.Deactivate(Now);
        }

        return tenant;
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class InMemoryTenantRepository : ITenantRepository
    {
        private readonly List<Tenant> _tenants;

        public InMemoryTenantRepository(params Tenant[] tenants) => _tenants = tenants.ToList();

        public Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tenants.FirstOrDefault(x => x.Id == id));

        public Task<(IReadOnlyList<Tenant> Items, int TotalCount)> ListAsync(TenantQuery query, CancellationToken cancellationToken = default)
        {
            IEnumerable<Tenant> source = _tenants;
            if (!query.IncludeInactive)
            {
                source = source.Where(x => x.IsActive);
            }

            var items = source.ToList();
            return Task.FromResult<(IReadOnlyList<Tenant>, int)>((items, items.Count));
        }

        public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            _tenants.Add(tenant);
            return Task.CompletedTask;
        }

        public void Update(Tenant tenant)
        {
        }
    }

    private sealed class InMemoryOrganizationIntegrationRepository : IOrganizationIntegrationRepository
    {
        private readonly List<OrganizationIntegration> _items = [];

        public int Count => _items.Count;

        public int CountFor(Guid organizationId, IntegrationProviderId providerId) =>
            _items.Count(x => x.OrganizationId == organizationId && x.ProviderId == providerId);

        public Task<OrganizationIntegration?> GetByOrganizationAndProviderAsync(
            Guid organizationId, IntegrationProviderId providerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(x => x.OrganizationId == organizationId && x.ProviderId == providerId));

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAsync(
            Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationIntegration>>(_items.Where(x => x.OrganizationId == organizationId).ToList());

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAndCategoryAsync(
            Guid organizationId, IntegrationProviderCategory category, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationIntegration>>(_items.Where(x => x.OrganizationId == organizationId).ToList());

        public Task AddAsync(OrganizationIntegration integration, CancellationToken cancellationToken = default)
        {
            _items.Add(integration);
            return Task.CompletedTask;
        }

        public void Update(OrganizationIntegration integration)
        {
        }

        public void Remove(OrganizationIntegration integration) => _items.Remove(integration);
    }
}
