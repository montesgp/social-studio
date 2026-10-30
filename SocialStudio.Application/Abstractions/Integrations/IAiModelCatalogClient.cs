using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Integrations;

/// <summary>
/// Provider anti-corruption port for listing models available to a concrete credential.
/// Implementations live in Infrastructure and map provider-specific DTOs into this stable shape.
/// </summary>
public interface IAiModelCatalogClient
{
    Task<Result<IReadOnlyList<AiProviderModel>>> ListModelsAsync(
        CancellationToken cancellationToken = default);
}

public sealed record AiProviderModel(
    string ModelId,
    string DisplayName,
    IReadOnlyDictionary<string, string> Metadata);

