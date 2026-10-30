using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;

namespace SocialStudio.Infrastructure.Integrations;

public sealed class IntegrationCredentialResolver : IIntegrationCredentialResolver
{
    private readonly IIntegrationResolver _integrationResolver;

    public IntegrationCredentialResolver(IIntegrationResolver integrationResolver)
    {
        _integrationResolver = integrationResolver;
    }

    public async Task<Result<ResolvedProviderCredential>> ResolveAsync(
        Guid organizationId,
        Domain.Integrations.IntegrationRouteCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _integrationResolver.ResolveProviderAsync(
            organizationId,
            candidate.ProviderKey,
            cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<ResolvedProviderCredential>(resolved.Error);
        }

        var integration = resolved.Value;
        return Result.Success(new ResolvedProviderCredential(
            candidate.ProviderId,
            integration.ProviderKey,
            integration.ConnectionMode,
            integration.Credentials,
            candidate.SelectedModel ?? integration.SelectedAiModel));
    }
}
