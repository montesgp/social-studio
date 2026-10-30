namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for reading CASE C aesthetic descriptors from the profile-analysis aggregate.
/// Returns the <c>DetectedBackgroundImages</c> text entries from the most recent completed
/// <c>ProfileAiAnalysis</c> for the given client.
///
/// <para>
/// CONTRACT (PA-SEAM-01, PA-SEAM-03):
/// <list type="bullet">
/// <item>No Apify call — this is a read-only adapter over already-stored AI analysis data.</item>
/// <item>Returns an empty list when no completed analysis exists — callers degrade gracefully.</item>
/// <item>No Infrastructure types in signature; the adapter lives in Infrastructure.</item>
/// </list>
/// </para>
///
/// Refs: design §4.4; BSP-04; PA-SEAM-01, PA-SEAM-02, PA-SEAM-03.
/// </summary>
public interface IClientAestheticDescriptorReader
{
    /// <summary>
    /// Returns the detected background-image descriptor strings for the given client from the most
    /// recent completed analysis record. Returns an empty list when no completed record exists,
    /// or when the most recent record has no descriptors (PA-SEAM-01b).
    /// </summary>
    /// <param name="orgId">The organization that owns the client.</param>
    /// <param name="clientId">The client whose profile analysis is queried.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<string>> GetDescriptorsAsync(Guid orgId, Guid clientId, CancellationToken ct);
}
