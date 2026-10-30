using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;

namespace SocialStudio.Application.Abstractions.Integrations;

public interface IIntegrationCredentialResolver
{
    Task<Result<ResolvedProviderCredential>> ResolveAsync(
        Guid organizationId,
        IntegrationRouteCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed record ResolvedProviderCredential(
    Guid ProviderId,
    string ProviderKey,
    IntegrationConnectionMode ConnectionMode,
    IReadOnlyDictionary<string, string> Credentials,
    string? SelectedModel);
