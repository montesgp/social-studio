using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Infrastructure.Integrations;

/// <summary>
/// Seeds a placeholder <c>(single-analyzer, content-discovery, discover-niche-content)</c> step
/// configuration for every ACTIVE organization that already has a CONNECTED Apify integration, so
/// the discovery stage appears pre-created in the settings UI (INT-DISC-02). The seeded row is
/// bound to the organization's existing Apify integration but carries <c>ActorId: null</c> and
/// <c>IsEnabled: false</c> — the platform never forces an actor (design D13, CD-12); the operator
/// only has to choose an actor and enable the stage. Idempotent: an organization that already has
/// ANY row for this step (operator-configured or previously seeded) is skipped, so a manual
/// configuration is never overwritten. Organizations without a connected data-extraction
/// integration get nothing seeded — there would be no integration to bind the row to.
/// <para>
/// IMPORTANT: <c>single-analyzer</c> already carries REAL operator configuration for
/// <c>content-analysis</c> and <c>profile-analysis</c>. <see cref="IWorkflowStepConfigurationRepository.ReplaceWorkflowAsync"/>
/// replaces ALL rows for the given (organization, workflowKey) pair, so this seeder reads the
/// existing rows first and concatenates the new placeholder onto them — never calling
/// ReplaceWorkflowAsync with a partial set — or it would silently wipe that operator setup.
/// </para>
/// Mirrors <see cref="ComfyUiOrganizationSeeder"/> / <see cref="ClaudePromptCliOrganizationSeeder"/>.
/// Refs: INT-DISC-02, D11, D13, CD-12; design §8.1.
/// </summary>
public sealed class TikTokNicheSearchWorkflowStepSeeder
{
    private const string ApifyProviderKey = "apify";
    private const string WorkflowKey = "single-analyzer";
    private const string AnalysisType = "content-discovery";
    private const string StepKey = "discover-niche-content";
    private const string OperationKey = "niche-search";
    private const string NetworkKey = "tiktok";

    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IWorkflowStepConfigurationRepository _stepConfigurations;
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public TikTokNicheSearchWorkflowStepSeeder(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IWorkflowStepConfigurationRepository stepConfigurations,
        ITenantRepository tenants,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _providers = providers;
        _integrations = integrations;
        _stepConfigurations = stepConfigurations;
        _tenants = tenants;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var provider = await _providers.GetByKeyAsync(ApifyProviderKey, cancellationToken);
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

            var integration = await _integrations.GetByOrganizationAndProviderAsync(
                tenant.Id.Value,
                provider.Id,
                cancellationToken);

            if (integration is null || integration.Status != IntegrationStatus.Connected)
            {
                // No connected data-extraction integration to bind the placeholder row to.
                continue;
            }

            var existingRows = await _stepConfigurations.ListAsync(tenant.Id.Value, WorkflowKey, cancellationToken);
            var alreadyConfigured = existingRows.Any(x =>
                string.Equals(x.AnalysisType, AnalysisType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.StepKey, StepKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.NetworkKey, NetworkKey, StringComparison.OrdinalIgnoreCase));

            if (alreadyConfigured)
            {
                // Never overwrite: this is either a previous seed or an operator's own configuration.
                continue;
            }

            var now = _clock.UtcNow;
            var nextPriority = existingRows.Count == 0 ? 1 : existingRows.Max(x => x.Priority) + 1;

            var seededRow = new WorkflowStepConfigurationRecord(
                Guid.NewGuid(),
                tenant.Id.Value,
                WorkflowKey,
                AnalysisType,
                StepKey,
                OperationKey,
                NetworkKey,
                provider.Id.Value,
                provider.Key,
                integration.Id.Value,
                ModelId: null,
                ActorId: null,
                Priority: nextPriority,
                IsEnabled: false,
                now,
                now);

            var rows = existingRows.Concat([seededRow]).ToList();
            await _stepConfigurations.ReplaceWorkflowAsync(tenant.Id.Value, WorkflowKey, rows, cancellationToken);
            seededAny = true;
        }

        if (seededAny)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
