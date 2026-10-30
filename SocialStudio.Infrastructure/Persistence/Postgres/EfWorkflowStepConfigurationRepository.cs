using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfWorkflowStepConfigurationRepository : IWorkflowStepConfigurationRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfWorkflowStepConfigurationRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<WorkflowStepConfigurationRecord>> ListAsync(
        Guid organizationId,
        string? workflowKey = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.OrganizationWorkflowStepConfigurations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(workflowKey))
        {
            var normalized = Normalize(workflowKey);
            query = query.Where(x => x.WorkflowKey == normalized);
        }

        var rows = await query
            .OrderBy(x => x.WorkflowKey)
            .ThenBy(x => x.AnalysisType)
            .ThenBy(x => x.StepKey)
            .ThenBy(x => x.NetworkKey)
            .ThenBy(x => x.Priority)
            .ToListAsync(cancellationToken);

        return rows.Select(ToRecord).ToList();
    }

    public async Task<IReadOnlyList<WorkflowStepConfigurationRecord>> ListForStepAsync(
        WorkflowStepResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var workflowKey = Normalize(request.WorkflowKey);
        var analysisType = Normalize(request.AnalysisType);
        var stepKey = Normalize(request.StepKey);
        var operationKey = NormalizeOrNull(request.OperationKey);
        var networkKey = NormalizeOrNull(request.NetworkKey);

        var rows = await _dbContext.OrganizationWorkflowStepConfigurations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.WorkflowKey == workflowKey)
            .Where(x => x.AnalysisType == analysisType)
            .Where(x => x.StepKey == stepKey)
            .Where(x => operationKey == null || x.OperationKey == operationKey)
            .Where(x => networkKey == null || x.NetworkKey == networkKey)
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);

        return rows.Select(ToRecord).ToList();
    }

    public async Task ReplaceWorkflowAsync(
        Guid organizationId,
        string workflowKey,
        IReadOnlyList<WorkflowStepConfigurationRecord> rows,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkflowKey = Normalize(workflowKey);
        var existing = await _dbContext.OrganizationWorkflowStepConfigurations
            .Where(x => x.OrganizationId == organizationId && x.WorkflowKey == normalizedWorkflowKey)
            .ToListAsync(cancellationToken);

        _dbContext.OrganizationWorkflowStepConfigurations.RemoveRange(existing);
        await _dbContext.OrganizationWorkflowStepConfigurations.AddRangeAsync(
            rows.Select(ToData),
            cancellationToken);
    }

    private static WorkflowStepConfigurationRecord ToRecord(OrganizationWorkflowStepConfigurationData row) =>
        new(
            row.Id,
            row.OrganizationId,
            row.WorkflowKey,
            row.AnalysisType,
            row.StepKey,
            row.OperationKey,
            row.NetworkKey,
            row.ProviderId,
            row.ProviderKey,
            row.OrganizationIntegrationId,
            row.ModelId,
            row.ActorId,
            row.Priority,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static OrganizationWorkflowStepConfigurationData ToData(WorkflowStepConfigurationRecord row) =>
        new()
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            WorkflowKey = row.WorkflowKey,
            AnalysisType = row.AnalysisType,
            StepKey = row.StepKey,
            OperationKey = row.OperationKey,
            NetworkKey = row.NetworkKey,
            ProviderId = row.ProviderId,
            ProviderKey = row.ProviderKey,
            OrganizationIntegrationId = row.OrganizationIntegrationId,
            ModelId = row.ModelId,
            ActorId = row.ActorId,
            Priority = row.Priority,
            IsEnabled = row.IsEnabled,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
        };

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string? NormalizeOrNull(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}

