namespace Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// Port over the append-only <c>social.discovered_candidates</c> table (design §5.1, D6, CD-08).
/// Implemented by <c>EfContentCandidateRepository</c> in Infrastructure (WU-5).
/// </summary>
public interface IContentCandidateRepository
{
    /// <summary>
    /// Persists one run's observations. The SAME candidate observed twice inside one call (same
    /// <see cref="CandidateObservation.CandidateKey"/> + <see cref="CandidateObservation.ObservedAtUtc"/>)
    /// is recorded exactly once — a double-write inside one run is rejected, never silently duplicated
    /// (CD-08).
    /// </summary>
    Task RecordObservationsAsync(
        Guid organizationId,
        Guid clientId,
        IReadOnlyList<CandidateObservation> observations,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the MOST RECENT observation for each requested <c>candidate_key</c>, scoped to the
    /// tenant/client. Keys with no prior observation are simply absent from the result.
    /// </summary>
    Task<IReadOnlyList<CandidateObservation>> GetLatestByKeysAsync(
        Guid organizationId,
        Guid clientId,
        IReadOnlyList<string> candidateKeys,
        CancellationToken ct = default);

    /// <summary>
    /// Reads the NICHE CANDIDATE BANK — the reserve a niche search serves from instead of dispatching
    /// a paid actor run.
    ///
    /// Scoped by ORGANIZATION + NETWORK + HASHTAG and deliberately NOT by client: two clients of the
    /// same organization working the same niche must share one paid scrape rather than buying it twice.
    /// The hashtags are the tokens <c>NicheHashtagDerivation</c> already produces (lowercased,
    /// de-accented, connector words dropped), so wordings that genuinely overlap converge on the same
    /// reserve.
    ///
    /// A candidate matches when it was banked under ANY of <paramref name="hashtags"/>. Returns the
    /// strongest candidates first, at most <paramref name="maxResults"/> of them, and an EMPTY list
    /// when nothing is stored — which is the ONLY condition (besides an explicit operator refresh)
    /// that may lead to a billed run.
    /// </summary>
    Task<IReadOnlyList<CandidateObservation>> GetNicheBankAsync(
        Guid organizationId,
        string networkKey,
        IReadOnlyList<string> hashtags,
        int maxResults,
        CancellationToken ct = default);

    /// <summary>
    /// Banks what a niche run brought back, against <c>(organizationId, networkKey, hashtags)</c>.
    ///
    /// Dedup is the SAME <c>CandidateIdentity</c> the chain uses in-run, so re-observing a candidate
    /// already in the bank never duplicates it. A candidate already stored keeps its ORIGINAL
    /// observation snapshot — the bank never silently rewrites what it already paid for, so the
    /// freshness the operator is shown stays true — but the hashtag set it is reachable under is
    /// widened, so a later run under a different wording does not have to buy it again.
    ///
    /// Returns how many NEW candidates were banked.
    /// </summary>
    Task<int> RecordNicheBankAsync(
        Guid organizationId,
        string networkKey,
        IReadOnlyList<string> hashtags,
        IReadOnlyList<CandidateObservation> observations,
        CancellationToken ct = default);
}

/// <summary>
/// One persisted observation of a discovered candidate (design §5.1). <c>Classification</c> is the
/// plain smallint the entity stores (0 Undetermined / 1 Trending / 2 Proven) rather than the
/// Application-layer <c>ContentClassification</c> enum, so this port stays free of a dependency on the
/// Features namespace.
/// </summary>
public sealed record CandidateObservation(
    string CandidateKey,
    string NetworkKey,
    string SourceKey,
    string SearchCriterion,
    string ContentUrl,
    string AuthorHandle,
    string Title,
    string? Summary,
    string? ThumbnailUrl,
    DateTimeOffset? PublishedAtUtc,
    long? Views,
    long? Likes,
    long? Comments,
    long? Shares,
    long? Saves,
    double? OpportunityScore,
    short? Classification,
    DateTimeOffset ObservedAtUtc);
