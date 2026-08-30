using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface IClientReferentAccountRepository
{
    Task<ClientReferentAccount?> GetByIdAsync(
        ClientReferentAccountId id,
        Guid organizationId,
        CancellationToken ct);

    Task<(IReadOnlyList<ClientReferentAccount> Items, int TotalCount)> ListByClientAsync(
        ClientId clientId,
        Guid organizationId,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken ct);

    /// <summary>
    /// Returns all accounts for a client (no pagination) — used by reorder and other aggregate operations.
    /// </summary>
    Task<IReadOnlyList<ClientReferentAccount>> GetAllByClientAsync(
        ClientId clientId,
        Guid organizationId,
        CancellationToken ct);

    Task<bool> ExistsByProfileUrlAsync(
        ClientId clientId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        ClientReferentAccountId? excludeId,
        CancellationToken ct);

    Task AddAsync(ClientReferentAccount referentAccount, CancellationToken ct);

    void Update(ClientReferentAccount referentAccount);

    /// <summary>
    /// Loads all accounts for a client as tracked, applies the Reorder domain logic,
    /// and persists in a single SaveChanges call (atomic, last-write-wins per ADR-2).
    /// </summary>
    Task ReorderAsync(
        ClientId clientId,
        Guid organizationId,
        IReadOnlyList<Guid> orderedIds,
        CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
