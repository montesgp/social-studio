using SocialStudio.Domain.Social.ClientCompetitorAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;

namespace SocialStudio.Application.Abstractions.Social;

public interface IClientCompetitorAccountRepository
{
    Task<ClientCompetitorAccount?> GetByIdAsync(
        ClientCompetitorAccountId id,
        Guid organizationId,
        CancellationToken ct);

    Task<(IReadOnlyList<ClientCompetitorAccount> Items, int TotalCount)> ListByClientAsync(
        ClientId clientId,
        Guid organizationId,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken ct);

    /// <summary>
    /// Returns all accounts for a client (no pagination) — used by reorder and other aggregate operations.
    /// </summary>
    Task<IReadOnlyList<ClientCompetitorAccount>> GetAllByClientAsync(
        ClientId clientId,
        Guid organizationId,
        CancellationToken ct);

    Task<bool> ExistsByProfileUrlAsync(
        ClientId clientId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        ClientCompetitorAccountId? excludeId,
        CancellationToken ct);

    Task AddAsync(ClientCompetitorAccount competitorAccount, CancellationToken ct);

    void Update(ClientCompetitorAccount account);

    /// <summary>
    /// Loads all accounts for a client as tracked, applies the Reorder domain logic,
    /// and persists in a single SaveChanges call.
    /// </summary>
    Task ReorderAsync(
        ClientId clientId,
        Guid organizationId,
        IReadOnlyList<Guid> orderedIds,
        CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
