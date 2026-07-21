using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Application.Abstractions.Social.Publications;

/// <summary>
/// Persistence port for <see cref="Publication"/> aggregates. All access is organization-scoped.
/// </summary>
public interface IPublicationRepository
{
    Task<Publication?> GetByIdAsync(Guid organizationId, PublicationId id, CancellationToken ct);

    /// <summary>Publications for a set of soft-referenced content items (Bank/Planner projection join).</summary>
    Task<IReadOnlyList<Publication>> ListByContentItemsAsync(
        Guid organizationId, IReadOnlyList<Guid> contentItemIds, CancellationToken ct);

    /// <summary>All publications for a client, across all content items and destinations.</summary>
    Task<IReadOnlyList<Publication>> ListByClientAsync(Guid organizationId, Guid clientId, CancellationToken ct);

    Task AddAsync(Publication publication, CancellationToken ct);

    Task UpdateAsync(Publication publication, CancellationToken ct);
}
