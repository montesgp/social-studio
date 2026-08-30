using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientAccountConnections;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Application.Features.Social.Publications;

/// <summary>
/// Two-level credential resolution service (design §6.2):
/// 1. Load the per-client <see cref="ClientAccountConnection"/> (account id + ciphertext token).
/// 2. Resolve org-level app access via the existing <see cref="IIntegrationResolver"/> — already
///    fails with <c>IntegrationNotConnected</c> when the org integration is not <c>Connected</c>, so
///    no duplicate status check is written here.
/// 3. Unprotect the per-account token via <see cref="IIntegrationSecretProtector"/>.
/// 4. Return the unprotected token bounded to the caller's single publish call. Never stored,
///    never logged, never returned in any DTO beyond this scope.
/// </summary>
public sealed class PublicationDestinationResolver : IPublicationDestinationResolver
{
    private readonly IClientAccountConnectionRepository _connections;
    private readonly IIntegrationResolver _integrationResolver;
    private readonly IIntegrationSecretProtector _protector;

    public PublicationDestinationResolver(
        IClientAccountConnectionRepository connections,
        IIntegrationResolver integrationResolver,
        IIntegrationSecretProtector protector)
    {
        _connections = connections;
        _integrationResolver = integrationResolver;
        _protector = protector;
    }

    public async Task<Result<ResolvedPublicationCredentials>> ResolveAsync(
        Guid organizationId, PublicationDestination destination, CancellationToken ct)
    {
        var connection = await _connections.GetByIdAsync(
            organizationId, new ClientAccountConnectionId(destination.ClientAccountConnectionId), ct);

        if (connection is null)
        {
            return Result.Failure<ResolvedPublicationCredentials>(PublicationErrors.DestinationUnresolved);
        }

        // Per-client level guard (PUB-04): a Disabled/Failed connection is "not connected" — fail before
        // unprotecting the token or hitting the provider. The org-level check is delegated to the resolver below.
        if (connection.Status != ClientAccountConnectionStatus.Connected)
        {
            return Result.Failure<ResolvedPublicationCredentials>(PublicationErrors.DestinationUnresolved);
        }

        // Org-level app access. ResolveProviderAsync already fails with IntegrationNotConnected when
        // Status != Connected and verifies category "publishing" — no duplicate check here.
        var resolvedIntegration = await _integrationResolver.ResolveProviderAsync(
            organizationId, destination.NetworkKey, ct);

        if (resolvedIntegration.IsFailure)
        {
            return Result.Failure<ResolvedPublicationCredentials>(PublicationErrors.DestinationUnresolved);
        }

        // Unprotect only for this call — never stored on the aggregate, never logged, never returned.
        var accessToken = _protector.Unprotect(connection.EncryptedAccessToken);

        return Result.Success(new ResolvedPublicationCredentials(
            connection.ExternalAccountId, accessToken, destination.NetworkKey));
    }
}
