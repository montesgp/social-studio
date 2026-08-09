using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Batches;

/// <summary>
/// Adapts the full analysis repository down to the one question the batch handler asks.
/// Thin on purpose — the data access stays in the existing repository, this only narrows the surface.
/// </summary>
public sealed class BatchAnalysisLookup : IBatchAnalysisLookup
{
    private readonly ISingleContentAnalysisRepository _analyses;

    public BatchAnalysisLookup(ISingleContentAnalysisRepository analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        _analyses = analyses;
    }

    public async Task<bool> ExistsAsync(Guid analysisId, Guid organizationId, CancellationToken ct)
    {
        // Tenant-scoped by the repository, so a cross-tenant id reads as "does not exist".
        var analysis = await _analyses.GetByIdAsync(analysisId, organizationId, ct);
        return analysis is not null;
    }
}

/// <summary>
/// Resolves the per-client piece cap from the client record.
///
/// This is the seam that later becomes billing-aware: swapping this implementation for one that
/// reads a plan or a usage budget changes nothing in the batch handler.
/// </summary>
public sealed class ClientBatchPieceCapLookup : IBatchPieceCapLookup
{
    private readonly ISocialClientRepository _clients;

    public ClientBatchPieceCapLookup(ISocialClientRepository clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        _clients = clients;
    }

    public async Task<int?> GetClientPieceCapAsync(Guid clientId, Guid organizationId, CancellationToken ct)
    {
        var client = await _clients.GetByIdAsync(new ClientId(clientId), organizationId, ct);

        // Null means "no client override"; the caller falls back to the platform default rather than
        // treating a missing value as unlimited.
        return client?.MaxPiecesPerGeneration;
    }
}
