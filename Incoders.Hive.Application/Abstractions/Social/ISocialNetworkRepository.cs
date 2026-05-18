using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Social.Networks;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface ISocialNetworkRepository
{
    Task<SocialNetwork?> GetByIdAsync(SocialNetworkId id, CancellationToken cancellationToken);

    Task<SocialNetwork?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    Task<PageResult<SocialNetwork>> ListAsync(SocialNetworkListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Batch-loads social networks by their ids. Used by ListByClient handler to avoid N+1.
    /// </summary>
    Task<IReadOnlyList<SocialNetwork>> GetByIdsAsync(IReadOnlyCollection<SocialNetworkId> ids, CancellationToken cancellationToken);

    Task AddAsync(SocialNetwork network, CancellationToken cancellationToken);

    void Update(SocialNetwork network);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record SocialNetworkListQuery(
    bool IncludeInactive,
    int Page,
    int PageSize,
    string? SearchTerm);
