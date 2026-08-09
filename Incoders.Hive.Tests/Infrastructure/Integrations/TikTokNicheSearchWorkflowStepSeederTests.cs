using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Tenants;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Tests.Application.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Infrastructure.Integrations;

/// <summary>
/// WU-6 RED tests (task 6.14): <c>TikTokNicheSearchWorkflowStepSeeder</c> seeds a placeholder
/// <c>discover-niche-content</c> step configuration, bound to the organization's already-connected
/// Apify integration, with <c>ActorId: null</c> and <c>IsEnabled: false</c> — never a forced actor
/// (INT-DISC-02, D13, CD-12). Idempotent: never overwrites an operator's own configuration and never
/// seeds anything for an organization with no connected data-extraction integration.
/// </summary>
public sealed class TikTokNicheSearchWorkflowStepSeederTests
{
    private static readonly DateTime Now = new(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Seeds_DisabledUnassignedStep_ForOrganizationWithConnectedApify()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        await new IntegrationCatalogSeeder(providers, new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var tenant = NewTenant("Org A");
        var tenants = new InMemoryTenantRepository(tenant);

        var apify = await providers.GetByKeyAsync("apify");
        Assert.NotNull(apify);

        var integrations = new InMemoryOrganizationIntegrationRepository();
        var apifyIntegration = ConnectedIntegration(tenant.Id.Value, apify!.Id);
        await integrations.AddAsync(apifyIntegration);

        var stepConfigurations = new InMemoryWorkflowStepConfigurationRepository();

        var seeder = new TikTokNicheSearchWorkflowStepSeeder(
            providers,
            integrations,
            stepConfigurations,
            tenants,
            new NoopUnitOfWork(),
            new TestClock(Now));

        await seeder.SeedAsync();

        var rows = await stepConfigurations.ListAsync(tenant.Id.Value, "generator");
        var seeded = Assert.Single(rows, x =>
            x.AnalysisType == "content-discovery" && x.StepKey == "discover-niche-content");

        Assert.Equal("niche-search", seeded.OperationKey);
        Assert.Equal("tiktok", seeded.NetworkKey);
        Assert.Equal("apify", seeded.ProviderKey);
        Assert.Equal(apifyIntegration.Id.Value, seeded.OrganizationIntegrationId);
        Assert.Null(seeded.ActorId);
        Assert.False(seeded.IsEnabled);
    }

    [Fact]
    public async Task SkipsOrganizations_WithNoConnectedDataExtractionIntegration()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        await new IntegrationCatalogSeeder(providers, new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var tenant = NewTenant("Org B — no Apify connection");
        var tenants = new InMemoryTenantRepository(tenant);
        var integrations = new InMemoryOrganizationIntegrationRepository();
        var stepConfigurations = new InMemoryWorkflowStepConfigurationRepository();

        var seeder = new TikTokNicheSearchWorkflowStepSeeder(
            providers,
            integrations,
            stepConfigurations,
            tenants,
            new NoopUnitOfWork(),
            new TestClock(Now));

        await seeder.SeedAsync();

        Assert.Empty(await stepConfigurations.ListAsync(tenant.Id.Value, "generator"));
    }

    [Fact]
    public async Task IsIdempotent_NeverOverwritesAnExistingConfiguration()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        await new IntegrationCatalogSeeder(providers, new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var tenant = NewTenant("Org C");
        var tenants = new InMemoryTenantRepository(tenant);
        var apify = await providers.GetByKeyAsync("apify");
        var integrations = new InMemoryOrganizationIntegrationRepository();
        var apifyIntegration = ConnectedIntegration(tenant.Id.Value, apify!.Id);
        await integrations.AddAsync(apifyIntegration);

        var stepConfigurations = new InMemoryWorkflowStepConfigurationRepository();

        // The operator already configured and enabled the niche-search step with a real actor.
        await stepConfigurations.ReplaceWorkflowAsync(
            tenant.Id.Value,
            "generator",
            [
                new WorkflowStepConfigurationRecord(
                    Guid.NewGuid(),
                    tenant.Id.Value,
                    "generator",
                    "content-discovery",
                    "discover-niche-content",
                    "niche-search",
                    "tiktok",
                    apify!.Id.Value,
                    "apify",
                    apifyIntegration.Id.Value,
                    ModelId: null,
                    ActorId: "operator-configured-actor",
                    Priority: 1,
                    IsEnabled: true,
                    Now,
                    Now)
            ]);

        var seeder = new TikTokNicheSearchWorkflowStepSeeder(
            providers,
            integrations,
            stepConfigurations,
            tenants,
            new NoopUnitOfWork(),
            new TestClock(Now));

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var rows = await stepConfigurations.ListAsync(tenant.Id.Value, "generator");
        var row = Assert.Single(rows, x =>
            x.AnalysisType == "content-discovery" && x.StepKey == "discover-niche-content");

        Assert.Equal("operator-configured-actor", row.ActorId);
        Assert.True(row.IsEnabled);
    }

    [Fact]
    public async Task DoesNotDisturb_OtherStepConfigurationsUnderTheSameWorkflow()
    {
        var providers = new CanvaConnectorSeederTests.InMemorySeederProviderRepository();
        await new IntegrationCatalogSeeder(providers, new NoopUnitOfWork(), new TestClock(Now)).SeedAsync();

        var tenant = NewTenant("Org D");
        var tenants = new InMemoryTenantRepository(tenant);
        var apify = await providers.GetByKeyAsync("apify");
        var integrations = new InMemoryOrganizationIntegrationRepository();
        var apifyIntegration = ConnectedIntegration(tenant.Id.Value, apify!.Id);
        await integrations.AddAsync(apifyIntegration);

        var stepConfigurations = new InMemoryWorkflowStepConfigurationRepository();

        var existingIdeaRow = new WorkflowStepConfigurationRecord(
            Guid.NewGuid(),
            tenant.Id.Value,
            "generator",
            "batch-generation",
            "discover-reference-content",
            "profile",
            "instagram",
            apify!.Id.Value,
            "apify",
            apifyIntegration.Id.Value,
            ModelId: null,
            ActorId: "reference-actor",
            Priority: 1,
            IsEnabled: true,
            Now,
            Now);

        await stepConfigurations.ReplaceWorkflowAsync(tenant.Id.Value, "generator", [existingIdeaRow]);

        var seeder = new TikTokNicheSearchWorkflowStepSeeder(
            providers,
            integrations,
            stepConfigurations,
            tenants,
            new NoopUnitOfWork(),
            new TestClock(Now));

        await seeder.SeedAsync();

        var rows = await stepConfigurations.ListAsync(tenant.Id.Value, "generator");
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.StepKey == "discover-reference-content" && x.ActorId == "reference-actor");
        Assert.Contains(rows, x => x.StepKey == "discover-niche-content" && x.ActorId == null);
    }

    private static Tenant NewTenant(string name)
    {
        return Tenant.Create(new TenantId(Guid.NewGuid()), name, Guid.NewGuid(), Now).Value;
    }

    private static OrganizationIntegration ConnectedIntegration(Guid organizationId, IntegrationProviderId providerId) =>
        OrganizationIntegration.Create(
            new OrganizationIntegrationId(Guid.NewGuid()),
            organizationId,
            providerId,
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Connected,
            isReadOnly: false,
            selectedAiModel: null,
            lastTestedAtUtc: Now,
            lastError: null,
            Now).Value;

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

    private sealed class InMemoryWorkflowStepConfigurationRepository : IWorkflowStepConfigurationRepository
    {
        private readonly List<WorkflowStepConfigurationRecord> _items = [];

        public Task<IReadOnlyList<WorkflowStepConfigurationRecord>> ListAsync(
            Guid organizationId,
            string? workflowKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowStepConfigurationRecord>>(
                _items
                    .Where(x => x.OrganizationId == organizationId)
                    .Where(x => string.IsNullOrWhiteSpace(workflowKey)
                        || string.Equals(x.WorkflowKey, workflowKey, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Priority)
                    .ToList());

        public Task<IReadOnlyList<WorkflowStepConfigurationRecord>> ListForStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowStepConfigurationRecord>>(
                _items
                    .Where(x => x.OrganizationId == request.OrganizationId)
                    .Where(x => string.Equals(x.WorkflowKey, request.WorkflowKey, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.AnalysisType, request.AnalysisType, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.StepKey, request.StepKey, StringComparison.OrdinalIgnoreCase))
                    .Where(x => x.IsEnabled)
                    .OrderBy(x => x.Priority)
                    .ToList());

        public Task ReplaceWorkflowAsync(
            Guid organizationId,
            string workflowKey,
            IReadOnlyList<WorkflowStepConfigurationRecord> rows,
            CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(x =>
                x.OrganizationId == organizationId
                && string.Equals(x.WorkflowKey, workflowKey, StringComparison.OrdinalIgnoreCase));
            _items.AddRange(rows);
            return Task.CompletedTask;
        }
    }
}
