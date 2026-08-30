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

    /// <summary>
    /// The organization's run history, newest first. Runs are already persisted per organization; this is
    /// what makes them reachable again after the operator closes the tool, instead of a result that only
    /// exists for as long as the browser tab does.
    ///
    /// Returns EVERY status, failures included: "the run I started yesterday broke" is exactly the thing
    /// a history has to be able to answer.
    /// </summary>
    Task<IReadOnlyList<DiscoveryRun>> ListByOrganizationAsync(Guid organizationId, int limit, CancellationToken ct = default);
}
