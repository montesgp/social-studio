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

    Task AddRangeAsync(IReadOnlyList<ContentItem> items, CancellationToken ct);

    Task UpdateAsync(ContentItem item, CancellationToken ct);

    Task DeleteAsync(Guid organizationId, ContentItemId id, CancellationToken ct);
}
