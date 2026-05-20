using Incoders.Hive.Domain.Social.ClientStrategies;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface IClientStrategyRepository
{
    Task<ClientStrategy?> GetByClientIdAsync(Guid clientId, Guid organizationId, CancellationToken ct);

    void Add(ClientStrategy strategy);

    void Update(ClientStrategy strategy);

    Task SaveChangesAsync(CancellationToken ct);
}
