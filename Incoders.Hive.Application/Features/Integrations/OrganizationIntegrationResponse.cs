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
    string? SelectedAiModel,
    DateTime? LastTestedAtUtc,
    string? LastError,
    bool SupportsBringYourOwnKey,
    bool SupportsManagedByIncoders,
    bool SupportsSystemDefault,
    bool SystemDefaultReadOnly,
    IReadOnlyList<string> AvailableModels,
    IReadOnlyList<IntegrationCredentialFieldResponse> CredentialFields,
    IReadOnlyList<string> ConfiguredCredentialKeys,
    IReadOnlyDictionary<string, string> CredentialValues,
    // True only on the provider's PRIMARY-category card when the provider exposes a model catalog
    // client — i.e. this card can pull its live model list from the provider and cache it (e.g.
    // OpenAI→AI models, ComfyUI→Media generation). Drives the "Sync models" affordance in the UI.
    bool SupportsModelSync = false);
