using Incoders.Hive.Domain.Social.MonthlyObjectives;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface IMonthlyObjectiveRepository
{
    Task<MonthlyObjective?> GetByClientAndMonthAsync(
        Guid clientId,
        Guid organizationId,
        int year,
        int month,
        CancellationToken ct);

    /// <summary>
    /// Returns all objectives for a client ordered by Year descending, Month descending.
    /// </summary>
    Task<IReadOnlyList<MonthlyObjective>> ListByClientAsync(
        Guid clientId,
        Guid organizationId,
        CancellationToken ct);

    void Add(MonthlyObjective objective);

    void Update(MonthlyObjective objective);

    Task SaveChangesAsync(CancellationToken ct);
}
