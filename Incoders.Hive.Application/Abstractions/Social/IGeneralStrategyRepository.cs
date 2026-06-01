using Incoders.Hive.Domain.Social.GeneralStrategies;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface IGeneralStrategyRepository
{
    Task<GeneralStrategy?> GetByClientIdAsync(Guid clientId, Guid organizationId, CancellationToken ct);

    void Add(GeneralStrategy strategy);

    void Update(GeneralStrategy strategy);

    Task SaveChangesAsync(CancellationToken ct);
}
