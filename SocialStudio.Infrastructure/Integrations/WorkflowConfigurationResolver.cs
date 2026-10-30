using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;

namespace SocialStudio.Infrastructure.Integrations;

public sealed class WorkflowConfigurationResolver : IWorkflowConfigurationResolver
{
    private readonly IWorkflowStepConfigurationRepository _repository;
    private readonly IOrganizationIntegrationRepository _integrations;

    public WorkflowConfigurationResolver(
        IWorkflowStepConfigurationRepository repository,
        IOrganizationIntegrationRepository integrations)
    {
        _repository = repository;
        _integrations = integrations;
    }

    public async Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<WorkflowStepRouteCandidate>>(IntegrationErrors.OrganizationRequired);
        }

        var rows = await _repository.ListForStepAsync(request, cancellationToken);
        if (rows.Count == 0)
        {
            return Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]);
        }

        var candidates = new List<WorkflowStepRouteCandidate>();
        foreach (var row in rows.OrderBy(x => x.Priority))
        {
            var integration = await _integrations.GetByOrganizationAndProviderAsync(
                request.OrganizationId,
                new IntegrationProviderId(row.ProviderId),
                cancellationToken);

            if (integration is null || integration.Id.Value != row.OrganizationIntegrationId)
            {
                continue;
            }

            if (integration.Status != IntegrationStatus.Connected)
            {
                continue;
            }

            candidates.Add(new WorkflowStepRouteCandidate(
                row.ProviderId,
                row.ProviderKey,
                row.OrganizationIntegrationId,
                integration.ConnectionMode,
                row.Priority,
                row.ModelId,
                row.ActorId,
                row.IsEnabled));
        }

        return Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(candidates);
    }
}

