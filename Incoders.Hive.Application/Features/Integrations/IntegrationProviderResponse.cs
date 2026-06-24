using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Features.Integrations;

public sealed record IntegrationProviderResponse(
    Guid Id,
    string Key,
    string DisplayName,
    IntegrationProviderCategory Category,
    IReadOnlyList<IntegrationProviderCategory> Categories,
    string? Description,
    string IconKey,
    bool SupportsBringYourOwnKey,
    bool SupportsManagedByIncoders,
    bool SupportsSystemDefault,
    bool SystemDefaultReadOnly,
    IReadOnlyList<IntegrationCredentialFieldResponse> CredentialFields,
    string[] AvailableModels,
    IReadOnlyDictionary<string, string[]> ModelsByCategory)
{
    public static IntegrationProviderResponse FromDomain(IntegrationProvider provider) =>
        new(
            provider.Id.Value,
            provider.Key,
            provider.DisplayName,
            provider.Category,
            provider.Categories,
            provider.Description,
            provider.IconKey,
            provider.SupportsBringYourOwnKey,
            provider.SupportsManagedByIncoders,
            provider.SupportsSystemDefault,
            provider.SystemDefaultReadOnly,
            provider.CredentialFields.Select(IntegrationCredentialFieldResponse.FromDomain).ToList(),
            [.. provider.AvailableModels],
            provider.ModelsByCategory.ToDictionary(
                x => x.Key.ToString(),
                x => x.Value.ToArray()));
}
