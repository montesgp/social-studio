namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Narrow read port: does this analysis exist for this tenant?
///
/// Deliberately smaller than <c>ISingleContentAnalysisRepository</c>. The batch handler needs one
/// boolean, and depending on the full repository would let it reach for data it has no business
/// touching — and would force every test to stand up a twenty-member fake.
/// </summary>
public interface IBatchAnalysisLookup
{
    Task<bool> ExistsAsync(Guid analysisId, Guid organizationId, CancellationToken ct);
}

/// <summary>
/// Narrow read port for the per-client piece cap.
///
/// This is the extension seam for billing: today the number comes from the client record, tomorrow
/// it can come from a plan, an entitlement service, or a usage budget, and the batch handler does not
/// change. Returns null when the client sets no override, in which case the platform default applies.
/// </summary>
public interface IBatchPieceCapLookup
{
    Task<int?> GetClientPieceCapAsync(Guid clientId, Guid organizationId, CancellationToken ct);
}
