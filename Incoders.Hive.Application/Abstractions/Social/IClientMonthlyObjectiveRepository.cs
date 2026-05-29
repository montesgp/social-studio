using Incoders.Hive.Domain.Social.ClientMonthlyObjectives;

namespace Incoders.Hive.Application.Abstractions.Social;

public interface IClientMonthlyObjectiveRepository
{
    Task<ClientMonthlyObjective?> GetByClientAndMonthAsync(
        Guid clientId,
        Guid organizationId,
        int year,
        int month,
        CancellationToken ct);

    /// <summary>
    /// Returns all objectives for a client ordered by Year descending, Month descending.
    /// </summary>
    Task<IReadOnlyList<ClientMonthlyObjective>> ListByClientAsync(
        Guid clientId,
        Guid organizationId,
        CancellationToken ct);

    void Add(ClientMonthlyObjective objective);

    void Update(ClientMonthlyObjective objective);

    Task SaveChangesAsync(CancellationToken ct);
}
