using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Integrations;

/// <summary>
/// Port: per-user Canva OAuth token storage.
///
/// <para>
/// Keyed by <c>(userId, organizationIntegrationId)</c>. There is at most one active
/// token pair per user per org-level Canva integration.
/// </para>
///
/// <para>
/// Implementations MUST persist ciphertext only — callers encrypt/decrypt via
/// <see cref="IIntegrationSecretProtector"/> before interacting with this port.
/// </para>
///
/// <para>
/// D2 concurrency: <see cref="UpsertAsync"/> may throw
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> when the
/// row's xmin has changed since it was last read. Callers implementing the refresh-token
/// rotation loop (see <c>CanvaOAuthService.EnsureFreshAsync</c>) must catch this exception
/// and re-read the row before retrying — they must NOT call Canva again on conflict.
/// </para>
/// </summary>
public interface ICanvaUserTokenRepository
{
    /// <summary>
    /// Returns the stored token for <paramref name="userId"/> under the given
    /// <paramref name="organizationIntegrationId"/>, or <c>null</c> if none exists.
    /// </summary>
    Task<CanvaUserToken?> GetAsync(
        Guid userId,
        Guid organizationIntegrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates the token row.
    /// The caller must call <c>SaveChangesAsync</c> (or use a unit-of-work) to commit.
    /// </summary>
    Task UpsertAsync(CanvaUserToken token, CancellationToken ct = default);

    /// <summary>
    /// Removes the token for <paramref name="userId"/> / <paramref name="organizationIntegrationId"/>.
    /// No-op if the row does not exist.
    /// The caller must call <c>SaveChangesAsync</c> to commit.
    /// </summary>
    Task DeleteAsync(
        Guid userId,
        Guid organizationIntegrationId,
        CancellationToken ct = default);
}
