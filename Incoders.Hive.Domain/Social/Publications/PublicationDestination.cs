namespace Incoders.Hive.Domain.Social.Publications;

/// <summary>
/// Where a <see cref="Publication"/> targets: which per-client publish account (a
/// <c>ClientAccountConnection</c>), on which social network, under which canonical provider key
/// (allowlist-validated in Application — v1: <c>"instagram"</c> only). Holds only soft ids and the
/// network key; no token, no provider type crosses into Domain.
/// </summary>
public sealed record PublicationDestination(
    Guid ClientAccountConnectionId,
    Guid SocialNetworkId,
    string NetworkKey);
