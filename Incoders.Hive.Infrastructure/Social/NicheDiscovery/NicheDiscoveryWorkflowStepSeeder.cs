using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// Auto-provisions the <c>niche-discovery</c> workflow steps for every organization that already has a
/// connected <c>youtube-data-api-v3</c> integration.
///
/// WHY THIS EXISTS: the origin resolves a route only when a step is enabled for the stage it is running.
/// Without provisioning, connecting the API key looks complete but every run dies with
/// <c>origin_unconfigured</c> on the first stage the operator never knew they had to enable — five
/// separate toggles for what is, to them, one decision: "use my YouTube key for discovery".
///
/// Upsert-on-missing-only: an explicit later disable, provider swap or priority edit survives restarts.
/// Nothing here overrides an operator's choice; it only fills in steps that were never configured.
/// </summary>
public sealed class NicheDiscoveryWorkflowStepSeeder
{
    private const string WorkflowKey = "niche-discovery";
    private const string AnalysisType = "market-opportunity";
    private const string NetworkKey = "youtube";
    private const string ProviderKey = "youtube-data-api-v3";

    private readonly AppIdentityDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<NicheDiscoveryWorkflowStepSeeder> _logger;

    public NicheDiscoveryWorkflowStepSeeder(
        AppIdentityDbContext db, IClock clock, ILogger<NicheDiscoveryWorkflowStepSeeder> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Every stage the origin can run — derived, so a stage added later cannot be forgotten.</summary>
    private static IEnumerable<string> StepKeys() =>
        Enum.GetValues<DiscoveryStage>().Select(NicheDiscoveryOriginConfiguration.StepKey);

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var provider = await _db.IntegrationProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == ProviderKey, cancellationToken);

        if (provider is null)
        {
            return;
        }

        var integrations = await _db.OrganizationIntegrations
            .AsNoTracking()
            .Where(i => i.ProviderId == provider.Id)
            .ToListAsync(cancellationToken);

        if (integrations.Count == 0)
        {
            return;
        }

        var stepKeys = StepKeys().ToList();
        var seeded = 0;

        foreach (var integration in integrations)
        {
            var existing = await _db.OrganizationWorkflowStepConfigurations
                .AsNoTracking()
                .Where(c => c.OrganizationId == integration.OrganizationId
                    && c.WorkflowKey == WorkflowKey
                    && c.AnalysisType == AnalysisType)
                .Select(c => c.StepKey)
                .ToListAsync(cancellationToken);

            var existingKeys = existing.ToHashSet(StringComparer.Ordinal);

            foreach (var stepKey in stepKeys)
            {
                if (existingKeys.Contains(stepKey))
                {
                    continue;
                }

                _db.OrganizationWorkflowStepConfigurations.Add(new OrganizationWorkflowStepConfigurationData
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = integration.OrganizationId,
                    WorkflowKey = WorkflowKey,
                    AnalysisType = AnalysisType,
                    StepKey = stepKey,
                    OperationKey = stepKey,
                    NetworkKey = NetworkKey,
                    ProviderId = provider.Id,
                    ProviderKey = ProviderKey,
                    OrganizationIntegrationId = integration.Id,
                    ModelId = null,
                    ActorId = null,
                    Priority = 1,
                    IsEnabled = true,
                    CreatedAtUtc = _clock.UtcNow,
                    UpdatedAtUtc = _clock.UtcNow,
                });

                seeded++;
            }
        }

        if (seeded == 0)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Auto-provisioned {Count} niche-discovery workflow step(s) for the connected YouTube data origin", seeded);
    }
}
