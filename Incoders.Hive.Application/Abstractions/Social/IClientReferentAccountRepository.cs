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

    Task<bool> ExistsByProfileUrlAsync(
        ClientId clientId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        ClientReferentAccountId? excludeId,
        CancellationToken ct);

    Task AddAsync(ClientReferentAccount referentAccount, CancellationToken ct);

    void Update(ClientReferentAccount referentAccount);

    Task SaveChangesAsync(CancellationToken ct);
}
