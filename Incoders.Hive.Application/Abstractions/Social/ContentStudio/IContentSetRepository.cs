using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Application.Abstractions.Social.ContentStudio;

/// <summary>
/// Persistence port for <see cref="ContentSet"/> aggregates. All access is organization-scoped.
/// </summary>
public interface IContentSetRepository
{
    Task<ContentSet?> GetByIdAsync(Guid organizationId, ContentSetId id, CancellationToken ct);

    /// <summary>Returns the GenerationRun set for a given source run, or null if not yet materialized.</summary>
    Task<ContentSet?> GetByRunAsync(Guid organizationId, Guid sourceRunId, CancellationToken ct);

    /// <summary>
    /// Maps each of the given item ids to the id of the set that owns it, via the set-membership
    /// join. Items with no owning set are simply absent from the result.
    /// </summary>
    /// <remarks>
    /// This is the ONLY reliable way to get from an item back to its set. A
    /// <see cref="ContentOrigin.SourceRunId"/> is the generation JOB id, whereas a GenerationRun set
    /// is keyed by a run id DERIVED from (analysisId, clientId) — the two never match, so resolving a
    /// set by an item's source run id always came back empty.
    /// Batched to keep a whole Content Bank listing off the N+1 path.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, Guid>> GetSetIdsByItemIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct);

    /// <summary>
    /// Removes the given items from every set that holds them, returning the number of membership
    /// rows deleted. Used by the purge path: membership rows carry no FK to the item, so deleting an
    /// item without this leaves a dangling row that makes the set report a member it cannot load.
    /// </summary>
    Task<int> RemoveItemMembershipsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct);

    Task AddAsync(ContentSet set, CancellationToken ct);

    Task UpdateAsync(ContentSet set, CancellationToken ct);

    /// <summary>Aggregates member item states into a roll-up summary (single query, not N item loads).</summary>
    Task<ReviewSummary> GetReviewSummaryAsync(Guid organizationId, ContentSetId setId, CancellationToken ct);
}
