using Incoders.Hive.Domain.Social.ContentStudio;

namespace Incoders.Hive.Application.Abstractions.Social.ContentStudio;

/// <summary>
/// Persistence port for <see cref="ContentItem"/> aggregates. All access is organization-scoped.
/// </summary>
public interface IContentItemRepository
{
    Task<ContentItem?> GetByIdAsync(Guid organizationId, ContentItemId id, CancellationToken ct);

    /// <summary>Items derived from a single generation run (ordered by creation).</summary>
    Task<IReadOnlyList<ContentItem>> ListByRunAsync(Guid organizationId, Guid sourceRunId, CancellationToken ct);

    /// <summary>Items that are members of a set, ordered by the set's member sort order.</summary>
    Task<IReadOnlyList<ContentItem>> ListBySetAsync(Guid organizationId, ContentSetId setId, CancellationToken ct);

    /// <summary>
    /// Client-wide Content Bank listing (design §5.1) — ALL items for a client, across every set and
    /// origin, optionally filtered by created-at range and/or review state. Ordered
    /// <c>created_at_utc DESC</c> (recency-weighted; last-7-days is a UI default, not a query
    /// constraint — <paramref name="filter"/> leaves both range bounds open unless supplied).
    /// </summary>
    Task<IReadOnlyList<ContentItem>> ListByClientAsync(
        Guid organizationId, Guid clientId, ContentBankFilter filter, CancellationToken ct);

    Task AddRangeAsync(IReadOnlyList<ContentItem> items, CancellationToken ct);

    Task UpdateAsync(ContentItem item, CancellationToken ct);

    Task DeleteAsync(Guid organizationId, ContentItemId id, CancellationToken ct);
}

/// <summary>Optional filters for <see cref="IContentItemRepository.ListByClientAsync"/> (design §5.1).
/// All bounds are optional — an empty filter returns the client's full history.</summary>
public sealed record ContentBankFilter(DateTime? FromUtc, DateTime? ToUtc, ReviewState? State)
{
    public static readonly ContentBankFilter Empty = new(null, null, null);
}
