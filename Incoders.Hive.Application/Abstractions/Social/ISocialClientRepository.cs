using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface ISocialClientRepository
{
    Task<Client?> GetByIdAsync(
        ClientId id,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(
        SocialClientQuery query,
        CancellationToken cancellationToken = default);

    Task AddAsync(Client client, CancellationToken cancellationToken = default);

    void Update(Client client);
}

public sealed record SocialClientQuery(
    Guid? OrganizationId,
    string? NameContains,
    bool IncludeInactive,
    int Page,
    int PageSize);
