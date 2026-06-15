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

    Task AddAsync(ContentSet set, CancellationToken ct);

    Task UpdateAsync(ContentSet set, CancellationToken ct);

    /// <summary>Aggregates member item states into a roll-up summary (single query, not N item loads).</summary>
    Task<ReviewSummary> GetReviewSummaryAsync(Guid organizationId, ContentSetId setId, CancellationToken ct);
}
