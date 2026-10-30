using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Application.Abstractions.Social;

public interface IClientBrandManualRepository
{
    Task<ClientBrandManual?> GetByIdAsync(
        ClientBrandManualId id,
        Guid organizationId,
        CancellationToken ct);

    Task<ClientBrandManual?> GetActiveByClientAsync(
        ClientId clientId,
        Guid organizationId,
        CancellationToken ct);

    Task<(IReadOnlyList<ClientBrandManual> Items, int TotalCount)> ListByClientAsync(
        ClientId clientId,
        Guid organizationId,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken ct);

    Task AddAsync(ClientBrandManual manual, CancellationToken ct);

    void Update(ClientBrandManual manual);

    Task SaveChangesAsync(CancellationToken ct);
}
