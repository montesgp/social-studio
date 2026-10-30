using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using MediatR;
using SocialStudio.Application.Features.Integrations;

namespace SocialStudio.Application.Features.Integrations.Configure;

public sealed class ConfigureOrganizationIntegrationHandler
    : IRequestHandler<ConfigureOrganizationIntegrationCommand, Result<OrganizationIntegrationResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IOrganizationIntegrationCredentialRepository _credentials;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ConfigureOrganizationIntegrationHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials,
        IIntegrationSecretProtector protector,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
        _protector = protector;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<OrganizationIntegrationResponse>> Handle(
        ConfigureOrganizationIntegrationCommand request,
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

        if (!provider.SupportsMode(request.ConnectionMode))
        {
            return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.ConnectionModeNotSupported);
        }

        var existing = await _integrations.GetByOrganizationAndProviderAsync(
            request.OrganizationId,
            provider.Id,
            cancellationToken);

        if (provider.SystemDefaultReadOnly && request.ConnectionMode == IntegrationConnectionMode.SystemDefault)
        {
            if (existing is not null)
            {
                await _credentials.RemoveByIntegrationIdAsync(existing.Id, cancellationToken);
                _integrations.Remove(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var fallback = IntegrationResponseFactory.From(
                request.OrganizationId,
                provider,
                null,
                Array.Empty<string>());

            return Result.Success(fallback);
        }

        var nowUtc = _clock.UtcNow;
        var isReadOnly = false;

        var integration = existing;
        var isNew = false;

        if (integration is null)
        {
            var created = OrganizationIntegration.Create(
                OrganizationIntegrationId.New(),
                request.OrganizationId,
                provider.Id,
                request.ConnectionMode,
                request.Enabled ? IntegrationStatus.Configured : IntegrationStatus.Disabled,
                isReadOnly,
                request.SelectedAiModel,
                null,
                null,
                nowUtc);

            if (created.IsFailure)
            {
                return Result.Failure<OrganizationIntegrationResponse>(created.Error);
            }

            integration = created.Value;
            isNew = true;
            await _integrations.AddAsync(integration, cancellationToken);
        }
        else
        {
            var configured = integration.Configure(
                request.ConnectionMode,
                isReadOnly,
                request.SelectedAiModel,
                request.Enabled,
                nowUtc);

            if (configured.IsFailure)
            {
                return Result.Failure<OrganizationIntegrationResponse>(configured.Error);
            }

        }

        var existingCredentials = isNew
            ? Array.Empty<OrganizationIntegrationCredential>()
            : await _credentials.ListByIntegrationIdAsync(integration.Id, cancellationToken);

        var mergedCredentials = existingCredentials.ToDictionary(
            x => x.Key,
            x => _protector.Unprotect(x.EncryptedValue),
            StringComparer.OrdinalIgnoreCase);

        foreach (var credential in request.Credentials)
        {
            var normalizedKey = credential.Key.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedKey) || string.IsNullOrWhiteSpace(credential.Value))
            {
                continue;
            }

            mergedCredentials[normalizedKey] = credential.Value.Trim();
        }

        if (request.Enabled && request.ConnectionMode != IntegrationConnectionMode.SystemDefault)
        {
            var missingRequired = provider.CredentialFields
                .Where(x => x.IsRequired)
                .Select(x => x.Key.Trim().ToLowerInvariant())
                .Any(key => !mergedCredentials.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value));

            if (missingRequired)
            {
                return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.RequiredCredentialMissing);
            }
        }

        foreach (var credential in request.Credentials)
        {
            var normalizedKey = credential.Key.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedKey) || string.IsNullOrWhiteSpace(credential.Value))
            {
                continue;
            }

            var protectedValue = _protector.Protect(credential.Value.Trim());
            var existingCredential = existingCredentials.FirstOrDefault(x =>
                string.Equals(x.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));

            if (existingCredential is not null)
            {
                var rotated = existingCredential.Rotate(protectedValue, nowUtc);
                if (rotated.IsFailure)
                {
                    return Result.Failure<OrganizationIntegrationResponse>(rotated.Error);
                }

                await _credentials.UpsertAsync(existingCredential, cancellationToken);
            }
            else
            {
                var createdCredential = OrganizationIntegrationCredential.Create(
                    OrganizationIntegrationCredentialId.New(),
                    integration.Id,
                    normalizedKey,
                    protectedValue,
                    nowUtc);

                if (createdCredential.IsFailure)
                {
                    return Result.Failure<OrganizationIntegrationResponse>(createdCredential.Error);
                }

                await _credentials.UpsertAsync(createdCredential.Value, cancellationToken);
            }
        }

        if (!isNew)
        {
            _integrations.Update(integration);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var response = IntegrationResponseFactory.From(
            request.OrganizationId,
            provider,
            integration,
            mergedCredentials.Keys.OrderBy(x => x).ToList());

        return Result.Success(response);
    }
}
