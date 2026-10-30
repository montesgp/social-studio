using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;

namespace SocialStudio.Infrastructure.Integrations;

/// <summary>
/// Auto-provisions the <c>claude-p</c> organization integration for every ACTIVE organization (tenant).
/// claude-p is backend-local and needs no per-organization credentials, so it can be pre-connected for the
/// whole platform. The seed is idempotent: organizations that already have a claude-p integration are
/// skipped, so it never overwrites a manual configuration. Only this connector is auto-seeded per-org.
/// </summary>
public sealed class ClaudePromptCliOrganizationSeeder
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ClaudePromptCliOrganizationSeeder(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        ITenantRepository tenants,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _providers = providers;
        _integrations = integrations;
        _tenants = tenants;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var provider = await _providers.GetByKeyAsync(ClaudePromptCliSettings.ProviderKey, cancellationToken);
        if (provider is null)
        {
            return;
        }

        var (tenants, _) = await _tenants.ListAsync(
            new TenantQuery(NameContains: null, IncludeInactive: false, Page: 1, PageSize: int.MaxValue),
            cancellationToken);

        var seededAny = false;
        foreach (var tenant in tenants)
        {
            if (!tenant.IsActive)
            {
                continue;
            }

            var existing = await _integrations.GetByOrganizationAndProviderAsync(tenant.Id.Value, provider.Id, cancellationToken);
            if (existing is not null)
            {
                continue;
            }

            // ManagedByIncoders (not SystemDefault): the connection tester runs the REAL claude-p health
            // check for this mode, so "Test connection" actually probes the host CLI. Seeded as Connected
            // so it is immediately selectable in the AI-model workflow stages.
            var integration = OrganizationIntegration.Create(
                new OrganizationIntegrationId(Guid.NewGuid()),
                tenant.Id.Value,
                provider.Id,
                IntegrationConnectionMode.ManagedByIncoders,
                IntegrationStatus.Connected,
                isReadOnly: false,
                selectedAiModel: null,
                lastTestedAtUtc: _clock.UtcNow,
                lastError: null,
                _clock.UtcNow);

            if (integration.IsFailure)
            {
                continue;
            }

            await _integrations.AddAsync(integration.Value, cancellationToken);
            seededAny = true;
        }

        if (seededAny)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
