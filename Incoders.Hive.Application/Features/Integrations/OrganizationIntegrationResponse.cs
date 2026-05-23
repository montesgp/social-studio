using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Features.Integrations;

public sealed record OrganizationIntegrationResponse(
    Guid? IntegrationId,
    Guid OrganizationId,
    string ProviderKey,
    string ProviderDisplayName,
    IntegrationProviderCategory Category,
    string IconKey,
    IntegrationConnectionMode ConnectionMode,
    IntegrationStatus Status,
    bool IsReadOnly,
    bool IsDefaultAi,
    string? SelectedAiModel,
    DateTime? LastTestedAtUtc,
    string? LastError,
    bool SupportsBringYourOwnKey,
    bool SupportsManagedByIncoders,
    bool SupportsSystemDefault,
    bool SystemDefaultReadOnly,
    IReadOnlyList<IntegrationCredentialFieldResponse> CredentialFields,
    IReadOnlyList<string> ConfiguredCredentialKeys,
    IReadOnlyDictionary<string, string> CredentialValues);
