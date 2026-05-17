using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.Delete;

public sealed class DeleteOrganizationIntegrationHandler : IRequestHandler<DeleteOrganizationIntegrationCommand, Result>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IOrganizationIntegrationCredentialRepository _credentials;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public DeleteOrganizationIntegrationHandler(
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

    public async Task<Result> Handle(DeleteOrganizationIntegrationCommand request, CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure(IntegrationErrors.OrganizationRequired);
        }

        var provider = await _providers.GetByKeyAsync(request.ProviderKey, cancellationToken);
        if (provider is null)
        {
            return Result.Failure(IntegrationErrors.ProviderNotFound);
        }

        var integration = await _integrations.GetByOrganizationAndProviderAsync(
            request.OrganizationId,
            provider.Id,
            cancellationToken);

        if (integration is null)
        {
            return provider.SystemDefaultReadOnly
                ? Result.Success()
                : Result.Failure(IntegrationErrors.IntegrationNotFound);
        }

        await _credentials.RemoveByIntegrationIdAsync(integration.Id, cancellationToken);
        _integrations.Remove(integration);

        if (provider.Category == IntegrationProviderCategory.AiModel && integration.IsDefaultAi)
        {
            var remainingAi = await _integrations.ListByOrganizationAndCategoryAsync(
                request.OrganizationId,
                IntegrationProviderCategory.AiModel,
                cancellationToken);

            var nextDefault = remainingAi
                .Where(x => x.Status == IntegrationStatus.Connected)
                .OrderBy(x => x.UpdatedAtUtc)
                .FirstOrDefault();

            if (nextDefault is not null)
            {
                nextDefault.SetDefaultAi(nextDefault.SelectedAiModel, _clock.UtcNow);
                _integrations.Update(nextDefault);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
