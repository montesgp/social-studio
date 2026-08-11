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
