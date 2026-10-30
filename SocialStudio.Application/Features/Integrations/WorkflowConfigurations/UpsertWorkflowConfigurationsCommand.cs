using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using MediatR;

namespace SocialStudio.Application.Features.Integrations.WorkflowConfigurations;

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

        if (WorkflowConfigurationCatalog.FindWorkflow(workflowKey) is null)
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

            if (analysisType.Length == 0 || stepKey.Length == 0)
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.ProviderRouteNotConfigured);
            }

            var step = WorkflowConfigurationCatalog.FindStep(workflowKey, analysisType, stepKey);
            if (step is null)
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.ProviderRouteNotConfigured);
            }

            var operationKey = step.OperationKey;
            var networkKey = step.RequiresNetwork
                ? NormalizeOrNull(input.NetworkKey)
                : null;

            if (step.RequiresNetwork && networkKey is null)
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.ProviderRouteNotConfigured);
            }

            if (networkKey is not null
                && step.SupportedNetworkKeys.Count > 0
                && !step.SupportedNetworkKeys.Contains(networkKey, StringComparer.OrdinalIgnoreCase))
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

            if (step.AllowedProviderCategories.Count > 0
                && !step.AllowedProviderCategories.Any(provider.ServesCategory))
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.ProviderRouteNotConfigured);
            }

            if (step.AllowedProviderKeys.Count > 0
                && !step.AllowedProviderKeys.Contains(provider.Key, StringComparer.OrdinalIgnoreCase))
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.ProviderRouteNotConfigured);
            }

            var integration = await _integrations.GetByOrganizationAndProviderAsync(
                request.OrganizationId,
                provider.Id,
                cancellationToken);

            if (integration is null || integration.Status != IntegrationStatus.Connected)
            {
                return Result.Failure<WorkflowConfigurationOverviewResponse>(IntegrationErrors.IntegrationNotConnected);
            }

            var modelId = step.SupportsModelSelection && !string.IsNullOrWhiteSpace(input.ModelId)
                ? input.ModelId.Trim()
                : null;
            var actorId = step.SupportsActorSelection && !string.IsNullOrWhiteSpace(input.ActorId)
                ? input.ActorId.Trim()
                : null;

            // Whether THIS step routes an AI model (not the provider's primary category): a shared
            // connector like OpenAI also serves MediaGeneration, so the synced-model requirement must
            // be keyed off the step's allowed categories, not provider.Category.
            var isAiModelStep = step.AllowedProviderCategories.Contains(IntegrationProviderCategory.AiModel);

            if (isAiModelStep)
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
            WorkflowConfigurationResponseMapper.ToCatalogResponse(),
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
