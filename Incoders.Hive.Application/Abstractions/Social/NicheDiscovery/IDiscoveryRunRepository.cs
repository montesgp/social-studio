using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Port over <c>social.discovery_runs</c> — the one NicheDiscovery table with UPDATEs (the status
/// lifecycle, design §9). Implemented by <c>EfDiscoveryRunRepository</c> in Infrastructure (WU-7b).
/// </summary>
public interface IDiscoveryRunRepository
{
    Task AddAsync(DiscoveryRun run, CancellationToken ct = default);

    Task UpdateAsync(DiscoveryRun run, CancellationToken ct = default);

    Task<DiscoveryRun?> GetByIdAsync(Guid organizationId, DiscoveryRunId id, CancellationToken ct = default);
}
