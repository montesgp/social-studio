using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Social.Niches;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfNicheRepository : INicheRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfNicheRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Niche?> GetByIdAsync(NicheId id, CancellationToken cancellationToken)
    {
        var row = await _dbContext.Niches
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<Niche?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = (code ?? string.Empty).Trim().ToLowerInvariant();

        var row = await _dbContext.Niches
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == normalized, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<Niche?> GetByDisplayNameAsync(string displayName, CancellationToken cancellationToken)
    {
        var normalized = (displayName ?? string.Empty).Trim();

        var row = await _dbContext.Niches
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DisplayName.ToLower() == normalized.ToLower(), cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<PageResult<Niche>> ListAsync(NicheListQuery query, CancellationToken cancellationToken)
    {
        IQueryable<NicheData> source = _dbContext.Niches.AsNoTracking();

        if (!query.IncludeInactive)
        {
            source = source.Where(x => x.IsActive);
        }

        var term = query.SearchTerm?.Trim();
        var hasTerm = !string.IsNullOrWhiteSpace(term);

        if (hasTerm)
        {
            source = source.Where(x =>
                EF.Functions.ILike(x.DisplayName, $"%{term}%") ||
                EF.Functions.ILike(x.Code, $"%{term}%"));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        // No search term means "show me the catalog": the newest entries are the useful ones, and
        // the page size is capped upstream so this never degenerates into a full table read.
        var ordered = hasTerm
            ? source.OrderBy(x => x.DisplayName)
            : source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.DisplayName);

        var rows = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<Niche>(
            rows.Select(ToDomain).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<IReadOnlyList<Niche>> GetByIdsAsync(
        IReadOnlyCollection<NicheId> ids,
        CancellationToken cancellationToken)
    {
        var idValues = ids.Select(id => id.Value).ToList();

        var rows = await _dbContext.Niches
            .AsNoTracking()
            .Where(x => idValues.Contains(x.Id))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToList();
    }

    public Task AddAsync(Niche niche, CancellationToken cancellationToken)
    {
        _dbContext.Niches.Add(ToData(niche));
        return Task.CompletedTask;
    }

    public void Update(Niche niche)
    {
        // Local-first tracked-entity pattern, mirroring EfSocialNetworkRepository.
        var entity = _dbContext.Niches.Local.FirstOrDefault(x => x.Id == niche.Id.Value)
                     ?? _dbContext.Niches.First(x => x.Id == niche.Id.Value);

        entity.DisplayName = niche.DisplayName;
        entity.IconKey = niche.IconKey;
        entity.SearchHashtags = niche.SearchHashtags.ToList();
        entity.IsActive = niche.IsActive;
        entity.UpdatedAtUtc = niche.UpdatedAtUtc;
        // entity.Code is intentionally NOT updated — Code is immutable post-create.
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPgUniqueViolation(ex))
        {
            // Defense-in-depth: the handler pre-checks code and display name, but a race can still
            // reach the DB unique index.
            throw new NicheUniquenessConflictException(ex);
        }
    }

    private static Niche ToDomain(NicheData row) =>
        Niche.Restore(
            new NicheId(row.Id),
            row.Code,
            row.DisplayName,
            row.IconKey,
            row.SearchHashtags,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static NicheData ToData(Niche n) =>
        new()
        {
            Id = n.Id.Value,
            Code = n.Code,
            DisplayName = n.DisplayName,
            IconKey = n.IconKey,
            SearchHashtags = n.SearchHashtags.ToList(),
            IsActive = n.IsActive,
            CreatedAtUtc = n.CreatedAtUtc,
            UpdatedAtUtc = n.UpdatedAtUtc,
        };

    private static bool IsPgUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "23505";
}

/// <summary>
/// Thrown when the platform.niches unique index on code or display name is violated.
/// </summary>
public sealed class NicheUniquenessConflictException : Exception
{
    public NicheUniquenessConflictException(Exception innerException)
        : base("A niche with this code or display name already exists.", innerException)
    {
    }
}
