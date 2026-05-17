using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.SetDefaultAi;

public sealed class SetDefaultAiIntegrationHandler
    : IRequestHandler<SetDefaultAiIntegrationCommand, Result<OrganizationIntegrationResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IOrganizationIntegrationCredentialRepository _credentials;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public SetDefaultAiIntegrationHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<OrganizationIntegrationResponse>> Handle(
        SetDefaultAiIntegrationCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.OrganizationRequired);
        }

        var provider = await _providers.GetByKeyAsync(request.ProviderKey, cancellationToken);
        if (provider is null)
        {
            return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.ProviderNotFound);
        }

        if (provider.Category != IntegrationProviderCategory.AiModel)
        {
            return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.DefaultAiProviderInvalidCategory);
        }

        var target = await _integrations.GetByOrganizationAndProviderAsync(request.OrganizationId, provider.Id, cancellationToken);
        if (target is null)
        {
            return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.IntegrationNotFound);
        }

        var setDefaultResult = target.SetDefaultAi(request.Model, _clock.UtcNow);
        if (setDefaultResult.IsFailure)
        {
            return Result.Failure<OrganizationIntegrationResponse>(setDefaultResult.Error);
        }

        var aiIntegrations = await _integrations.ListByOrganizationAndCategoryAsync(
            request.OrganizationId,
            IntegrationProviderCategory.AiModel,
            cancellationToken);

        foreach (var integration in aiIntegrations)
        {
            if (integration.Id == target.Id)
            {
                continue;
            }

            if (integration.IsDefaultAi)
            {
                integration.ClearDefaultAi(_clock.UtcNow);
                _integrations.Update(integration);
            }
        }

        _integrations.Update(target);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var configuredKeys = (await _credentials.ListByIntegrationIdAsync(target.Id, cancellationToken))
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToList();

        var response = IntegrationResponseFactory.From(
            request.OrganizationId,
            provider,
            target,
            configuredKeys);

        return Result.Success(response);
    }
}
