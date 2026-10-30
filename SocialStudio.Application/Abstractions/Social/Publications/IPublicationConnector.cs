using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Social.Publications;

/// <summary>
/// Channel-agnostic port for publishing content to an external social network. The seam that makes
/// Instagram-first reversible: adding Meta/TikTok/YouTube later is a new adapter registered under its
/// own <see cref="NetworkKey"/>, with zero change to the aggregate, commands, or this port.
///
/// <para>
/// Implementations live in Infrastructure and own every provider HTTP/SDK detail. Only domain and
/// application types (<see cref="ResolvedPublicationRequest"/>, <see cref="PublicationResult"/>,
/// <see cref="Result{TValue}"/>) cross this boundary.
/// </para>
/// </summary>
public interface IPublicationConnector
{
    /// <summary>Canonical provider key this connector serves, e.g. <c>"instagram"</c>. Used by
    /// <see cref="IPublicationConnectorSelector"/> to route a <c>PublicationDestination</c>.</summary>
    string NetworkKey { get; }

    Task<Result<PublicationResult>> PublishAsync(ResolvedPublicationRequest request, CancellationToken ct);
}

/// <summary>
/// A fully-resolved publish request, bounded to the single <see cref="IPublicationConnector.PublishAsync"/>
/// call. <see cref="AccessToken"/> is UNPROTECTED plaintext at this point — it is produced by
/// <c>PublicationDestinationResolver</c> for this call only and MUST NOT be persisted, logged, or
/// returned in any DTO.
/// </summary>
public sealed record ResolvedPublicationRequest(
    Guid OrganizationId,
    Guid ClientId,
    Guid ContentItemId,
    string ExternalAccountId,
    string AccessToken,
    string NetworkKey,
    string? Caption,
    IReadOnlyList<PublicationAssetRef> Assets)
{
    // Defense-in-depth: carries a plaintext AccessToken; never let the default record ToString() print it.
    public override string ToString() => "ResolvedPublicationRequest { [redacted] }";
}

/// <summary>A resolved, publicly-fetchable media reference the connector can hand to the provider.</summary>
public sealed record PublicationAssetRef(string Url, string Mime);

/// <summary>The provider's identifier for the published post/media, recorded as
/// <c>Publication.ExternalReference</c> on success.</summary>
public sealed record PublicationResult(string ExternalReference);
