namespace SocialStudio.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// READ-ONLY port over the discovery candidate bank (<c>social.discovered_candidates</c>, the shared
/// organization-scoped reserve) for the AI Analysis tool.
///
/// Deliberately separate from <see cref="IContentCandidateRepository"/>: that port exists to RECORD what
/// discovery paid for and to SERVE a niche shortlist. This one exists to let an operator interrogate the
/// whole bank. Keeping them apart means the read side can never accidentally acquire a write, and the
/// discovery start+poll model stays untouched.
///
/// It returns the bank as flat rows and nothing else. All ranking, filtering, counting and aggregation
/// happens in <c>DiscoveryBankQueryExecutor</c>, over the FULL set, deterministically — never in a model
/// and never in provider-specific SQL built from model output.
/// </summary>
public interface IDiscoveryBankReader
{
    /// <summary>
    /// Every banked candidate of one organization, newest observation per candidate key.
    /// Returns an EMPTY list when the organization has never run a discovery — the tool's first-class
    /// "no data yet" state, not an error.
    /// </summary>
    Task<IReadOnlyList<DiscoveryBankRow>> ListAsync(
        Guid organizationId,
        CancellationToken ct = default);
}

/// <summary>
/// One banked candidate as the AI Analysis tool sees it. A projection, not the persistence entity: the
/// bank hashtags are flattened here because "which hashtag was this banked under" is a question the
/// operator asks, and <see cref="CandidateObservation"/> does not carry them.
/// </summary>
public sealed record DiscoveryBankRow(
    string CandidateKey,
    string NetworkKey,
    IReadOnlyList<string> Hashtags,
    string ContentUrl,
    string AuthorHandle,
    string Title,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset ObservedAtUtc,
    long? Views,
    long? Likes,
    long? Comments,
    long? Shares,
    long? Saves,
    double? OpportunityScore);
