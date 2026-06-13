using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Features.Integrations;

internal static class IntegrationResponseFactory
{
    public static OrganizationIntegrationResponse From(
        Guid organizationId,
        IntegrationProvider provider,
        OrganizationIntegration? integration,
        IReadOnlyList<string> configuredCredentialKeys,
        IReadOnlyDictionary<string, string>? credentialValues = null,
        IReadOnlyList<string>? availableModels = null)
    {
        var providerResponse = IntegrationProviderResponse.FromDomain(provider);
        var values = credentialValues ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var models = availableModels ?? provider.AvailableModels;

        if (integration is null)
        {
            var fallbackStatus = provider.SystemDefaultReadOnly
                ? IntegrationStatus.Connected
                : IntegrationStatus.NotConfigured;

            var fallbackMode = provider.SystemDefaultReadOnly
                ? IntegrationConnectionMode.SystemDefault
                : provider.SupportsBringYourOwnKey
                    ? IntegrationConnectionMode.BringYourOwnKey
                    : provider.SupportsManagedByIncoders
                        ? IntegrationConnectionMode.ManagedByIncoders
                        : IntegrationConnectionMode.SystemDefault;

            return new OrganizationIntegrationResponse(
                null,
                organizationId,
                provider.Key,
                provider.DisplayName,
                provider.Category,
                provider.IconKey,
                fallbackMode,
                fallbackStatus,
                provider.SystemDefaultReadOnly,
                null,
                null,
                null,
                provider.SupportsBringYourOwnKey,
                provider.SupportsManagedByIncoders,
                provider.SupportsSystemDefault,
                provider.SystemDefaultReadOnly,
                models,
                providerResponse.CredentialFields,
                configuredCredentialKeys,
                values);
        }

        return new OrganizationIntegrationResponse(
            integration.Id.Value,
            integration.OrganizationId,
            provider.Key,
            provider.DisplayName,
            provider.Category,
            provider.IconKey,
            integration.ConnectionMode,
            integration.Status,
            integration.IsReadOnly,
            integration.SelectedAiModel,
            integration.LastTestedAtUtc,
            integration.LastError,
            provider.SupportsBringYourOwnKey,
            provider.SupportsManagedByIncoders,
            provider.SupportsSystemDefault,
            provider.SystemDefaultReadOnly,
            models,
            providerResponse.CredentialFields,
            configuredCredentialKeys,
            values);
    }
}
