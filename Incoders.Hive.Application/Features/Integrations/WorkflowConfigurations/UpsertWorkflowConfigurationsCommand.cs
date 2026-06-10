using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;

namespace Incoders.Hive.Application.Features.Integrations.WorkflowConfigurations;

public sealed record UpsertWorkflowConfigurationsCommand(
    Guid OrganizationId,
    string WorkflowKey,
    IReadOnlyList<WorkflowStepConfigurationInput> Steps)
    : IRequest<Result<WorkflowConfigurationOverviewResponse>>;

public sealed class UpsertWorkflowConfigurationsHandler
    : IRequestHandler<UpsertWorkflowConfigurationsCommand, Result<WorkflowConfigurationOverviewResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IWorkflowStepConfigurationRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IOrganizationAiModelRepository? _models;

    public UpsertWorkflowConfigurationsHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IWorkflowStepConfigurationRepository repository,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOrganizationAiModelRepository? models = null)
    {
        _providers = providers;
        _integrations = integrations;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _models = models;
    }

    public async Task<Result<WorkflowConfigurationOverviewResponse>> Handle(
        UpsertWorkflowConfigurationsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.OrganizationRequired);
        }

        var workflowKey = Normalize(request.WorkflowKey);
        if (workflowKey.Length == 0)
        {
            return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.ProviderRouteNotConfigured);
        }

        var rows = new List<WorkflowStepConfigurationRecord>();
        var now = _clock.UtcNow;
        var priorityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in request.Steps ?? [])
        {
            if (input.Priority <= 0)
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.RoutingCandidatePriorityInvalid);
            }

            var analysisType = Normalize(input.AnalysisType);
            var stepKey = Normalize(input.StepKey);
            var operationKey = NormalizeOrNull(input.OperationKey);
            var networkKey = NormalizeOrNull(input.NetworkKey);

            if (analysisType.Length == 0 || stepKey.Length == 0)
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.ProviderRouteNotConfigured);
            }

            var priorityKey = $"{analysisType}|{stepKey}|{operationKey ?? ""}|{networkKey ?? ""}|{input.Priority}";
            if (!priorityKeys.Add(priorityKey))
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.RoutingCandidatePriorityInvalid);
            }

            var providerKey = Normalize(input.ProviderKey);
            var provider = await _providers.GetByKeyAsync(providerKey, cancellationToken);
            if (provider is null)
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.ProviderNotFound);
            }

            var integration = await _integrations.GetByOrganizationAndProviderAsync(
                request.OrganizationId,
                provider.Id,
                cancellationToken);

            if (integration is null || integration.Status != IntegrationStatus.Connected)
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.IntegrationNotConnected);
            }

            var modelId = string.IsNullOrWhiteSpace(input.ModelId) ? null : input.ModelId.Trim();
            var actorId = string.IsNullOrWhiteSpace(input.ActorId) ? null : input.ActorId.Trim();

            if (provider.Category == IntegrationProviderCategory.AiModel)
            {
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.AiModelRequired);
                }

                if (_models is not null)
                {
                    var syncedModels = await _models.ListByProviderAsync(
                        request.OrganizationId,
                        provider.Id.Value,
                        cancellationToken);

                    if (syncedModels.Count > 0
                        && !syncedModels.Any(x => string.Equals(x.ModelId, modelId, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.AiModelNotAvailable);
                    }
                }
            }

            if (string.Equals(provider.Key, "apify", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(actorId))
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.ProviderRouteNotConfigured);
            }

            rows.Add(new WorkflowStepConfigurationRecord(
                Guid.NewGuid(),
                request.OrganizationId,
                workflowKey,
                analysisType,
                stepKey,
                operationKey,
                networkKey,
                provider.Id.Value,
                provider.Key,
                integration.Id.Value,
                modelId,
                actorId,
                input.Priority,
                input.IsEnabled,
                now,
                now));
        }

        await _repository.ReplaceWorkflowAsync(
            request.OrganizationId,
            workflowKey,
            rows,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _repository.ListAsync(request.OrganizationId, workflowKey, cancellationToken);
        return Result.Success(new WorkflowConfigurationOverviewResponse(
            request.OrganizationId,
            workflowKey,
            saved.Select(WorkflowConfigurationResponseMapper.ToResponse).ToList()));
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string? NormalizeOrNull(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
