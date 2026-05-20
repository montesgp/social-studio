using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;

namespace Incoders.Hive.Application.Features.Integrations.RoutingPolicies;

public sealed class UpsertOrganizationRoutingPolicyHandler
    : IRequestHandler<UpsertOrganizationRoutingPolicyCommand, Result<OrganizationRoutingPolicyResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IIntegrationRoutingPolicyRepository _routingPolicies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpsertOrganizationRoutingPolicyHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IIntegrationRoutingPolicyRepository routingPolicies,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _providers = providers;
        _integrations = integrations;
        _routingPolicies = routingPolicies;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<OrganizationRoutingPolicyResponse>> Handle(
        UpsertOrganizationRoutingPolicyCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<OrganizationRoutingPolicyResponse>(IntegrationErrors.OrganizationRequired);
        }

        var candidates = new List<IntegrationRouteCandidate>(request.Candidates.Count);

        foreach (var input in request.Candidates)
        {
            var provider = await _providers.GetByKeyAsync(input.ProviderKey, cancellationToken);
            if (provider is null)
            {
                return Result.Failure<OrganizationRoutingPolicyResponse>(IntegrationErrors.ProviderNotFound);
            }

            var integration = await _integrations.GetByOrganizationAndProviderAsync(
                request.OrganizationId,
                provider.Id,
                cancellationToken);

            IntegrationConnectionMode connectionMode;
            string? selectedModel;
            if (integration is null)
            {
                if (!provider.SystemDefaultReadOnly)
                {
                    return Result.Failure<OrganizationRoutingPolicyResponse>(IntegrationErrors.IntegrationNotFound);
                }

                connectionMode = IntegrationConnectionMode.SystemDefault;
                selectedModel = null;
            }
            else
            {
                if (integration.Status == IntegrationStatus.Disabled)
                {
                    return Result.Failure<OrganizationRoutingPolicyResponse>(IntegrationErrors.IntegrationDisabled);
                }

                connectionMode = integration.ConnectionMode;
                selectedModel = integration.SelectedAiModel;
            }

            var model = string.IsNullOrWhiteSpace(input.SelectedModel) ? selectedModel : input.SelectedModel;
            var candidate = IntegrationRouteCandidate.Create(
                provider.Id.Value,
                provider.Key,
                connectionMode,
                input.Priority,
                input.AllowManagedFallback,
                input.MaxAttempts,
                input.TimeoutMs,
                model,
                input.IsEnabled);

            if (candidate.IsFailure)
            {
                return Result.Failure<OrganizationRoutingPolicyResponse>(candidate.Error);
            }

            candidates.Add(candidate.Value);
        }

        var nowUtc = _clock.UtcNow;
        var created = IntegrationRoutingPolicy.Create(
            Guid.NewGuid(),
            request.OrganizationId,
            request.FlowKey,
            request.CapabilityKey,
            request.PlatformKey,
            request.IsEnabled,
            candidates,
            nowUtc);

        if (created.IsFailure)
        {
            return Result.Failure<OrganizationRoutingPolicyResponse>(created.Error);
        }

        await _routingPolicies.UpsertAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = (await _routingPolicies.ListByOrganizationAsync(
                request.OrganizationId,
                request.FlowKey,
                request.CapabilityKey,
                request.PlatformKey,
                cancellationToken))
            .OrderByDescending(policy => policy.UpdatedAtUtc)
            .FirstOrDefault();

        return updated is null
            ? Result.Failure<OrganizationRoutingPolicyResponse>(IntegrationErrors.RoutingPolicyNotConfigured)
            : Result.Success(OrganizationRoutingPolicyResponse.FromDomain(updated));
    }
}
