using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfSocialNetworkRepository : ISocialNetworkRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfSocialNetworkRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SocialNetwork?> GetByIdAsync(SocialNetworkId id, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SocialNetworks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<SocialNetwork?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SocialNetworks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<PageResult<SocialNetwork>> ListAsync(
        SocialNetworkListQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<SocialNetworkData> source = _dbContext.SocialNetworks.AsNoTracking();

        if (!query.IncludeInactive)
        {
            source = source.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            source = source.Where(x =>
                x.DisplayName.Contains(term) ||
                x.Code.Contains(term));
        }

        var totalCount = await source.CountAsync(cancellationToken);
        var rows = await source
            .OrderBy(x => x.DisplayName)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<SocialNetwork>(
            rows.Select(ToDomain).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<IReadOnlyList<SocialNetwork>> GetByIdsAsync(
        IReadOnlyCollection<SocialNetworkId> ids,
        CancellationToken cancellationToken)
    {
        var idValues = ids.Select(id => id.Value).ToList();
        var rows = await _dbContext.SocialNetworks
            .AsNoTracking()
            .Where(x => idValues.Contains(x.Id))
            .ToListAsync(cancellationToken);
        return rows.Select(ToDomain).ToList();
    }

    public Task AddAsync(SocialNetwork network, CancellationToken cancellationToken)
    {
        _dbContext.SocialNetworks.Add(ToData(network));
        return Task.CompletedTask;
    }

    public void Update(SocialNetwork network)
    {
        // Local-first tracked-entity pattern (ADR 10)
        var entity = _dbContext.SocialNetworks.Local.FirstOrDefault(x => x.Id == network.Id.Value)
                     ?? _dbContext.SocialNetworks.First(x => x.Id == network.Id.Value);

        entity.DisplayName = network.DisplayName;
        entity.IconKey = network.IconKey;
        entity.IsActive = network.IsActive;
        entity.UpdatedAtUtc = network.UpdatedAtUtc;
        // entity.Code is intentionally NOT updated here — ADR 6: Code is immutable post-create
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPgUniqueViolation(ex))
        {
            // Defense-in-depth: handler pre-checks via GetByCodeAsync, but race conditions
            // could still hit the DB unique constraint. Translate to domain error.
            throw new SocialNetworkCodeConflictException(ex);
        }
    }

    private static SocialNetwork ToDomain(SocialNetworkData row) =>
        SocialNetwork.Restore(
            new SocialNetworkId(row.Id),
            row.Code,
            row.DisplayName,
            row.IconKey,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static SocialNetworkData ToData(SocialNetwork n) =>
        new()
        {
            Id = n.Id.Value,
            Code = n.Code,
            DisplayName = n.DisplayName,
            IconKey = n.IconKey,
            IsActive = n.IsActive,
            CreatedAtUtc = n.CreatedAtUtc,
            UpdatedAtUtc = n.UpdatedAtUtc,
        };

    private static bool IsPgUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "23505";
}

/// <summary>
/// Thrown when the platform.social_networks unique constraint on code is violated.
/// Caught by CreateSocialNetworkHandler as defense-in-depth against race conditions.
/// </summary>
public sealed class SocialNetworkCodeConflictException : Exception
{
    public SocialNetworkCodeConflictException(Exception innerException)
        : base("A social network with this code already exists.", innerException)
    {
    }
}
