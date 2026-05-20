using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfIntegrationRoutingPolicyRepository : IIntegrationRoutingPolicyRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfIntegrationRoutingPolicyRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<IntegrationRoutingPolicy>> ListByOrganizationAsync(
        Guid organizationId,
        string? flowKey = null,
        string? capabilityKey = null,
        string? platformKey = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedFlow = NormalizeOptionalKey(flowKey);
        var normalizedCapability = NormalizeOptionalKey(capabilityKey);
        var normalizedPlatform = NormalizeOptionalKey(platformKey);

        var query = _dbContext.OrganizationIntegrationRoutingPolicies
            .AsNoTracking()
            .Include(x => x.Items)
                .ThenInclude(x => x.Provider)
            .Where(x => x.OrganizationId == organizationId);

        if (normalizedFlow is not null)
        {
            query = query.Where(x => x.FlowKey == normalizedFlow);
        }

        if (normalizedCapability is not null)
        {
            query = query.Where(x => x.CapabilityKey == normalizedCapability);
        }

        if (platformKey is not null)
        {
            query = query.Where(x => x.PlatformKey == normalizedPlatform);
        }

        var rows = await query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IntegrationRoutingPolicy?> GetEnabledPolicyAsync(
        Guid organizationId,
        string flowKey,
        string capabilityKey,
        string? platformKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedFlow = NormalizeRequiredKey(flowKey);
        var normalizedCapability = NormalizeRequiredKey(capabilityKey);
        var normalizedPlatform = NormalizeOptionalKey(platformKey);

        var rows = await _dbContext.OrganizationIntegrationRoutingPolicies
            .AsNoTracking()
            .Include(x => x.Items)
                .ThenInclude(x => x.Provider)
            .Where(x => x.OrganizationId == organizationId
                && x.FlowKey == normalizedFlow
                && x.CapabilityKey == normalizedCapability
                && x.IsEnabled
                && (x.PlatformKey == normalizedPlatform || (normalizedPlatform != null && x.PlatformKey == null)))
            .ToListAsync(cancellationToken);

        var row = rows
            .OrderByDescending(x => x.PlatformKey == normalizedPlatform)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefault();

        return row is null ? null : ToDomain(row);
    }

    public async Task UpsertAsync(
        IntegrationRoutingPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.OrganizationIntegrationRoutingPolicies
            .Include(x => x.Items)
            .FirstOrDefaultAsync(
                x => x.Id == policy.Id
                    || (x.OrganizationId == policy.OrganizationId
                        && x.FlowKey == policy.FlowKey
                        && x.CapabilityKey == policy.CapabilityKey
                        && x.PlatformKey == policy.PlatformKey),
                cancellationToken);

        if (existing is null)
        {
            _dbContext.OrganizationIntegrationRoutingPolicies.Add(ToData(policy));
            return;
        }

        existing.OrganizationId = policy.OrganizationId;
        existing.FlowKey = policy.FlowKey;
        existing.CapabilityKey = policy.CapabilityKey;
        existing.PlatformKey = policy.PlatformKey;
        existing.IsEnabled = policy.IsEnabled;
        existing.UpdatedAtUtc = policy.UpdatedAtUtc;

        _dbContext.OrganizationIntegrationRoutingPolicyItems.RemoveRange(existing.Items);
        existing.Items.Clear();

        foreach (var item in policy.Candidates.Select(candidate => ToDataItem(policy.Id, candidate, policy.CreatedAtUtc, policy.UpdatedAtUtc)))
        {
            existing.Items.Add(item);
        }
    }

    private static IntegrationRoutingPolicy ToDomain(OrganizationIntegrationRoutingPolicyData row)
    {
        var candidates = row.Items
            .OrderBy(x => x.Priority)
            .Select(ToCandidate)
            .ToList();

        return IntegrationRoutingPolicy.Restore(
            row.Id,
            row.OrganizationId,
            row.FlowKey,
            row.CapabilityKey,
            row.PlatformKey,
            row.IsEnabled,
            candidates,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
    }

    private static IntegrationRouteCandidate ToCandidate(OrganizationIntegrationRoutingPolicyItemData row) =>
        new(
            row.ProviderId,
            row.Provider?.Key ?? string.Empty,
            Enum.Parse<IntegrationConnectionMode>(row.ConnectionMode),
            row.Priority,
            row.AllowManagedFallback,
            row.MaxAttempts,
            row.TimeoutMs,
            row.SelectedModel,
            row.IsEnabled);

    private static OrganizationIntegrationRoutingPolicyData ToData(IntegrationRoutingPolicy policy) => new()
    {
        Id = policy.Id,
        OrganizationId = policy.OrganizationId,
        FlowKey = policy.FlowKey,
        CapabilityKey = policy.CapabilityKey,
        PlatformKey = policy.PlatformKey,
        IsEnabled = policy.IsEnabled,
        CreatedAtUtc = policy.CreatedAtUtc,
        UpdatedAtUtc = policy.UpdatedAtUtc,
        Items = policy.Candidates
            .Select(candidate => ToDataItem(policy.Id, candidate, policy.CreatedAtUtc, policy.UpdatedAtUtc))
            .ToList(),
    };

    private static OrganizationIntegrationRoutingPolicyItemData ToDataItem(
        Guid policyId,
        IntegrationRouteCandidate candidate,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        PolicyId = policyId,
        ProviderId = candidate.ProviderId,
        ConnectionMode = candidate.ConnectionMode.ToString(),
        Priority = candidate.Priority,
        AllowManagedFallback = candidate.AllowManagedFallback,
        MaxAttempts = candidate.MaxAttempts,
        TimeoutMs = candidate.TimeoutMs,
        SelectedModel = candidate.SelectedModel,
        IsEnabled = candidate.IsEnabled,
        CreatedAtUtc = createdAtUtc,
        UpdatedAtUtc = updatedAtUtc,
    };

    private static string NormalizeRequiredKey(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string? NormalizeOptionalKey(string? value)
    {
        var normalized = NormalizeRequiredKey(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
