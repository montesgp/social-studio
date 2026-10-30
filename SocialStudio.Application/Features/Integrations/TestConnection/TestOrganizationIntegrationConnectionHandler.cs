using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using MediatR;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.TestConnection;

public sealed class TestOrganizationIntegrationConnectionHandler
    : IRequestHandler<TestOrganizationIntegrationConnectionCommand, Result<OrganizationIntegrationResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IOrganizationIntegrationCredentialRepository _credentials;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IIntegrationConnectionTester _connectionTester;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public TestOrganizationIntegrationConnectionHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials,
        IIntegrationSecretProtector protector,
        IIntegrationConnectionTester connectionTester,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
        _protector = protector;
        _connectionTester = connectionTester;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<OrganizationIntegrationResponse>> Handle(
        TestOrganizationIntegrationConnectionCommand request,
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

        var integration = await _integrations.GetByOrganizationAndProviderAsync(
            request.OrganizationId,
            provider.Id,
            cancellationToken);

        if (integration is null)
        {
            if (provider.SystemDefaultReadOnly)
            {
                var readonlySystemDefault = IntegrationResponseFactory.From(
                    request.OrganizationId,
                    provider,
                    null,
                    Array.Empty<string>());

                return Result.Success(readonlySystemDefault);
            }

            return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.IntegrationNotFound);
        }

        if (integration.Status == IntegrationStatus.Disabled)
        {
            return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.IntegrationDisabled);
        }

        var credentials = await _credentials.ListByIntegrationIdAsync(integration.Id, cancellationToken);
        var plainCredentials = credentials.ToDictionary(
            x => x.Key,
            x => _protector.Unprotect(x.EncryptedValue),
            StringComparer.OrdinalIgnoreCase);

        IntegrationConnectionTestResult result;
        if (integration.ConnectionMode == IntegrationConnectionMode.SystemDefault)
        {
            result = new IntegrationConnectionTestResult(true);
        }
        else
        {
            result = await _connectionTester.TestAsync(provider, integration.ConnectionMode, plainCredentials, cancellationToken);
        }

        integration.MarkTestResult(result.IsSuccess, result.ErrorMessage, _clock.UtcNow);
        _integrations.Update(integration);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var response = IntegrationResponseFactory.From(
            request.OrganizationId,
            provider,
            integration,
            plainCredentials.Keys.OrderBy(x => x).ToList());

        return Result.Success(response);
    }
}
