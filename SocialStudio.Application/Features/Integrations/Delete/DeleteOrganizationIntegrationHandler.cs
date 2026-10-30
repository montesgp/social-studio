using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using MediatR;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.Delete;

public sealed class DeleteOrganizationIntegrationHandler : IRequestHandler<DeleteOrganizationIntegrationCommand, Result>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IOrganizationIntegrationCredentialRepository _credentials;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteOrganizationIntegrationHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials,
        IUnitOfWork unitOfWork)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
        _unitOfWork = unitOfWork;
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

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
