using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Start-and-poll execution queue (design §10), matching the single-analyzer idiom. Implemented by
/// <c>NicheDiscoveryRunQueue</c> in Infrastructure (WU-11a), drained by a hosted background service.
/// </summary>
public interface INicheDiscoveryRunQueue
{
    Task EnqueueAsync(DiscoveryRunId runId, CancellationToken ct = default);

    Task<DiscoveryRunId?> DequeueAsync(CancellationToken ct = default);
}
