namespace SocialStudio.Application.Abstractions.Social;

using SocialStudio.Domain.Social.Clients;

/// <summary>
/// Platform-admin lookup port that resolves a client without organization scoping.
/// This is deliberately separate from <see cref="ISocialClientRepository"/> so the tenant-scoped
/// <c>GetByIdAsync</c> stays the default for every client-scoped feature, and the unscoped escape
/// hatch stays explicit and greppable.
/// </summary>
/// <remarks>
/// This introduces no new reach: <see cref="ISocialClientRepository.ListAsync"/> already returns
/// clients across every organization when <c>SocialClientQuery.OrganizationId</c> is null, which is
/// how the platform-admin "All organizations" list works. Without an unscoped by-id lookup, that
/// list surfaced clients the by-id endpoints could not resolve, so opening one 404'd.
/// Callers MUST gate this behind a platform-admin check.
/// </remarks>
public interface ISocialClientDirectoryRepository
{
    /// <returns>The client, or <c>null</c> when no client exists with that id in any organization.</returns>
    Task<Client?> GetByIdAcrossOrganizationsAsync(
        ClientId id,
        CancellationToken cancellationToken = default);
}
