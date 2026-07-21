using Incoders.Hive.Domain.Social.ClientAccountConnections;

namespace Incoders.Hive.Application.Features.Social.Publications.Connections;

/// <summary>
/// Token-free projection of a <see cref="ClientAccountConnection"/>. NEVER includes the access
/// token — only the external account id, status, expiry, and last-test metadata.
/// </summary>
public sealed record ClientAccountConnectionResponse(
    Guid Id,
    Guid ClientId,
    Guid SocialNetworkId,
    Guid OrganizationIntegrationId,
    string ExternalAccountId,
    DateTime? TokenExpiresAtUtc,
    string Status,
    DateTime? LastTestedAtUtc,
    string? LastError,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static ClientAccountConnectionResponse FromDomain(ClientAccountConnection connection) => new(
        connection.Id.Value,
        connection.ClientId.Value,
        connection.SocialNetworkId.Value,
        connection.OrganizationIntegrationId.Value,
        connection.ExternalAccountId,
        connection.TokenExpiresAtUtc,
        connection.Status.ToString(),
        connection.LastTestedAtUtc,
        connection.LastError,
        connection.CreatedAtUtc,
        connection.UpdatedAtUtc);
}
