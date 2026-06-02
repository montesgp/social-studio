using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface IClientOwnAccountRepository
{
    Task<ClientOwnAccount?> GetByIdAsync(
        ClientOwnAccountId id,
        Guid organizationId,
        CancellationToken ct);

    Task<(IReadOnlyList<ClientOwnAccount> Items, int TotalCount)> ListByClientAsync(
        ClientId clientId,
        Guid organizationId,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken ct);

    /// <summary>
    /// Returns all accounts for a client (no pagination) — used by reorder and other aggregate operations.
    /// </summary>
    Task<IReadOnlyList<ClientOwnAccount>> GetAllByClientAsync(
        ClientId clientId,
        Guid organizationId,
        CancellationToken ct);

    Task<bool> ExistsByProfileUrlAsync(
        ClientId clientId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        ClientOwnAccountId? excludeId,
        CancellationToken ct);

    Task AddAsync(ClientOwnAccount ownAccount, CancellationToken ct);

    void Update(ClientOwnAccount account);

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
