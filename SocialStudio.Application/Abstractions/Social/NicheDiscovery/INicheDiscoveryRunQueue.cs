using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Start-and-poll execution queue (design §10), matching the single-analyzer idiom. Implemented by
/// <c>NicheDiscoveryRunQueue</c> in Infrastructure (WU-11a), drained by a hosted background service.
/// </summary>
/// <remarks>
/// GAP FIX (WU-11a): the WU-3 stub carried only <see cref="DiscoveryRunId"/>, but
/// <c>IDiscoveryRunRepository.GetByIdAsync</c> is tenant-scoped and needs an organization id the
/// background worker has no other way to obtain after a dequeue. Corrected here, before any
/// implementation or caller of this interface existed — mirrors <c>ProfileAnalysisJob</c>, which
/// already carries <c>OrganizationId</c> alongside its id for exactly this reason.
/// </remarks>
public interface INicheDiscoveryRunQueue
{
    Task EnqueueAsync(Guid organizationId, DiscoveryRunId runId, CancellationToken ct = default);

    Task<(Guid OrganizationId, DiscoveryRunId RunId)?> DequeueAsync(CancellationToken ct = default);
}
