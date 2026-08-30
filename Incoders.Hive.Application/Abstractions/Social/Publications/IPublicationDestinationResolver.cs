using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Application.Abstractions.Social.Publications;

/// <summary>
/// Two-level credential resolution service (design §6.2): turns a <see cref="PublicationDestination"/>
/// into the credential fields needed to build a <see cref="ResolvedPublicationRequest"/>. Loads the
/// per-client <c>ClientAccountConnection</c> AND the org-level app access via the existing
/// <c>IIntegrationResolver</c> — both are required for a valid provider call.
/// </summary>
public interface IPublicationDestinationResolver
{
    Task<Result<ResolvedPublicationCredentials>> ResolveAsync(
        Guid organizationId, PublicationDestination destination, CancellationToken ct);
}

/// <summary>
/// Credential fields resolved for a single publish call. <see cref="AccessToken"/> is UNPROTECTED
/// plaintext, bounded to the caller's immediate use (building a <c>ResolvedPublicationRequest</c>) —
/// never stored, never logged, never returned in a DTO.
/// </summary>
public sealed record ResolvedPublicationCredentials(
    string ExternalAccountId,
    string AccessToken,
    string NetworkKey)
{
    // Defense-in-depth: carries a plaintext AccessToken; never let the default record ToString() print it.
    public override string ToString() => "ResolvedPublicationCredentials { [redacted] }";
}
