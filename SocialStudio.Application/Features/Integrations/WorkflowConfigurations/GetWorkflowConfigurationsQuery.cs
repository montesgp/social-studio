using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using MediatR;

namespace SocialStudio.Application.Features.Integrations.WorkflowConfigurations;

public sealed record GetWorkflowConfigurationsQuery(
    Guid OrganizationId,
    string? WorkflowKey)
    : IRequest<Result<WorkflowConfigurationOverviewResponse>>;

public sealed class GetWorkflowConfigurationsHandler
    : IRequestHandler<GetWorkflowConfigurationsQuery, Result<WorkflowConfigurationOverviewResponse>>
{
    private readonly IWorkflowStepConfigurationRepository _repository;

    public GetWorkflowConfigurationsHandler(IWorkflowStepConfigurationRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<WorkflowConfigurationOverviewResponse>> Handle(
        GetWorkflowConfigurationsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.OrganizationRequired);
        }

        var rows = await _repository.ListAsync(
            request.OrganizationId,
            request.WorkflowKey,
            cancellationToken);

        return Result.Success(new WorkflowConfigurationOverviewResponse(
            request.OrganizationId,
            string.IsNullOrWhiteSpace(request.WorkflowKey) ? null : request.WorkflowKey.Trim().ToLowerInvariant(),
            WorkflowConfigurationResponseMapper.ToCatalogResponse(),
            rows.Select(WorkflowConfigurationResponseMapper.ToResponse).ToList()));
    }
}
