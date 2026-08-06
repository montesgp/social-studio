using Incoders.Hive.Domain.Social.ClientAccountConnections;
using Incoders.Hive.Domain.Social.Networks;

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
    DateTime UpdatedAtUtc,
    /// <summary>
    /// The target network's catalog code (e.g. <c>instagram</c>) — the value publish/schedule
    /// commands take as <c>NetworkKey</c> to select a connector. Without it on this projection the
    /// UI had only an opaque <see cref="SocialNetworkId"/> and hardcoded "instagram" for every
    /// destination, which silently published to the wrong connector for any other network.
    /// Empty when the referenced network row could not be resolved.
    /// </summary>
    string NetworkKey,
    /// <summary>Human label for the network, for destination pickers.</summary>
    string NetworkDisplayName,
    /// <summary>Catalog icon key for the network, so the picker stops hardcoding one icon.</summary>
    string NetworkIconKey)
{
    public static ClientAccountConnectionResponse FromDomain(
        ClientAccountConnection connection, SocialNetwork? network) => new(
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
        connection.UpdatedAtUtc,
        network?.Code ?? string.Empty,
        network?.DisplayName ?? string.Empty,
        network?.IconKey ?? string.Empty);
}
