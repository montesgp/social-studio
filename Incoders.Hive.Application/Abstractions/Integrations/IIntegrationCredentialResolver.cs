using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IIntegrationCredentialResolver
{
    Task<Result<ResolvedProviderCredential>> ResolveAsync(
        IntegrationRouteCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed record ResolvedProviderCredential(
    Guid ProviderId,
    string ProviderKey,
    IntegrationConnectionMode ConnectionMode,
    IReadOnlyDictionary<string, string> Credentials,
    string? SelectedModel);