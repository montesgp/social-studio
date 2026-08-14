using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Runs one <see cref="DiscoveryRunId"/> end to end: sourcing → analytics → semantics → market
/// comparison → scoring → append-only persistence → terminal status transition (design §1).
/// Implemented by <c>NicheDiscoveryRunProcessor</c> in Features (WU-11a). A thin seam over that
/// concrete class purely for the background worker's testability — mirrors how
/// <c>ProfileAnalysisBackgroundWorker</c> resolves <c>IProfileAnalyzer</c> rather than a concrete type.
/// </summary>
public interface INicheDiscoveryRunProcessor
{
    Task RunAsync(Guid organizationId, DiscoveryRunId runId, CancellationToken ct = default);
}
