using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Social.Niches;

namespace SocialStudio.Application.Abstractions.Social;

/// <summary>
/// Persistence port for the platform-managed niche/sector catalog.
/// </summary>
public interface INicheRepository
{
    Task<Niche?> GetByIdAsync(NicheId id, CancellationToken cancellationToken);

    Task<Niche?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Case-insensitive display-name lookup. The catalog is picked from a searchable list, so two
    /// entries reading the same to a human are a defect, not a valid state.
    /// </summary>
    Task<Niche?> GetByDisplayNameAsync(string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Bounded list. Never returns the whole catalog: the caller's page size is capped upstream and
    /// an empty search term deliberately resolves to the most recently created entries.
    /// </summary>
    Task<PageResult<Niche>> ListAsync(NicheListQuery query, CancellationToken cancellationToken);

    /// <summary>Batch-loads niches by id so client-association reads avoid N+1.</summary>
    Task<IReadOnlyList<Niche>> GetByIdsAsync(
        IReadOnlyCollection<NicheId> ids,
        CancellationToken cancellationToken);

    Task AddAsync(Niche niche, CancellationToken cancellationToken);

    void Update(Niche niche);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Bounded catalog query. A non-empty <paramref name="SearchTerm"/> filters by display name or code
/// and orders alphabetically; an empty one returns the most recently created entries first, so the
/// list stays useful (and small) as the catalog grows.
/// </summary>
public sealed record NicheListQuery(
    bool IncludeInactive,
    int Page,
    int PageSize,
    string? SearchTerm);
