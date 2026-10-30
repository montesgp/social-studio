using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICanvaUserTokenRepository"/>.
///
/// <para>
/// Persistence: <c>platform.canva_user_tokens</c> — one row per <c>(userId, organizationIntegrationId)</c>.
/// </para>
///
/// <para>
/// D2 concurrency: the table uses Postgres <c>xmin</c> as an optimistic concurrency token.
/// <see cref="UpsertAsync"/> will raise <see cref="DbUpdateConcurrencyException"/> if the row
/// was updated by another request since it was last read. Callers (e.g. <c>CanvaOAuthService.EnsureFreshAsync</c>)
/// must implement the reread-on-conflict retry described in design decision D2.
/// </para>
/// </summary>
public sealed class EfCanvaUserTokenRepository : ICanvaUserTokenRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfCanvaUserTokenRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<CanvaUserToken?> GetAsync(
        Guid userId,
        Guid organizationIntegrationId,
        CancellationToken ct = default)
    {
        var row = await _dbContext.CanvaUserTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.OrganizationIntegrationId == organizationIntegrationId,
                ct);

        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(CanvaUserToken token, CancellationToken ct = default)
    {
        var existing = await _dbContext.CanvaUserTokens
            .FirstOrDefaultAsync(
                x => x.UserId == token.UserId && x.OrganizationIntegrationId == token.OrganizationIntegrationId,
                ct);

        if (existing is null)
        {
            _dbContext.CanvaUserTokens.Add(ToData(token));
            return;
        }

        // Update mutable fields — xmin concurrency token is managed by Postgres/EF.
        existing.EncryptedAccessToken = token.EncryptedAccessToken;
        existing.EncryptedRefreshToken = token.EncryptedRefreshToken;
        existing.AccessTokenExpiresAtUtc = token.AccessTokenExpiresAtUtc;
        existing.Scopes = token.Scopes;
        existing.UpdatedAtUtc = token.UpdatedAtUtc;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        Guid userId,
        Guid organizationIntegrationId,
        CancellationToken ct = default)
    {
        var row = await _dbContext.CanvaUserTokens
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.OrganizationIntegrationId == organizationIntegrationId,
                ct);

        if (row is not null)
        {
            _dbContext.CanvaUserTokens.Remove(row);
        }
    }

    private static CanvaUserToken ToDomain(CanvaUserTokenData row) =>
        CanvaUserToken.Restore(
            new CanvaUserTokenId(row.Id),
            row.UserId,
            row.OrganizationIntegrationId,
            row.EncryptedAccessToken,
            row.EncryptedRefreshToken,
            row.AccessTokenExpiresAtUtc,
            row.Scopes,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static CanvaUserTokenData ToData(CanvaUserToken token) =>
        new()
        {
            Id = token.Id.Value,
            UserId = token.UserId,
            OrganizationIntegrationId = token.OrganizationIntegrationId,
            EncryptedAccessToken = token.EncryptedAccessToken,
            EncryptedRefreshToken = token.EncryptedRefreshToken,
            AccessTokenExpiresAtUtc = token.AccessTokenExpiresAtUtc,
            Scopes = token.Scopes,
            CreatedAtUtc = token.CreatedAtUtc,
            UpdatedAtUtc = token.UpdatedAtUtc,
        };
}
