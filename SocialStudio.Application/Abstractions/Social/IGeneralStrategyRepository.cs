using SocialStudio.Domain.Social.GeneralStrategies;

namespace SocialStudio.Application.Abstractions.Social;

public interface IGeneralStrategyRepository
{
    Task<GeneralStrategy?> GetByClientIdAsync(Guid clientId, Guid organizationId, CancellationToken ct);

    void Add(GeneralStrategy strategy);

    void Update(GeneralStrategy strategy);

    Task SaveChangesAsync(CancellationToken ct);
}
