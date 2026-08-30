using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;

namespace Incoders.Hive.Infrastructure.Integrations;

/// <summary>
/// Auto-provisions the <c>comfyui</c> organization integration for every ACTIVE organization
/// (tenant). ComfyUI is a local, zero-marginal-cost backend with no per-organization API key, so it
/// can be pre-connected for the whole platform under <see cref="IntegrationConnectionMode.SystemDefault"/>
/// — the same shape as the catalog's <c>comfyui</c> <c>SeedDefinition</c>. The seed is idempotent:
/// organizations that already have a comfyui integration are skipped, so it never overwrites a
/// manual configuration. Mirrors <see cref="ClaudePromptCliOrganizationSeeder"/>.
/// Refs: CUI-INT-02, CUI-INT-02-a, CUI-INT-02-b; design §2.2.
/// </summary>
public sealed class ComfyUiOrganizationSeeder
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ComfyUiOrganizationSeeder(
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
        var provider = await _providers.GetByKeyAsync(ComfyUiSettings.ProviderKey, cancellationToken);
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

            // SystemDefault: comfyui points at the host's local server by default (base_url falls
            // back to http://127.0.0.1:8188). Connectivity is informational — CUI-INT-03 — so the
            // record is seeded as Connected and never blocks job scheduling.
            var integration = OrganizationIntegration.Create(
                new OrganizationIntegrationId(Guid.NewGuid()),
                tenant.Id.Value,
                provider.Id,
                IntegrationConnectionMode.SystemDefault,
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
