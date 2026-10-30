using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF implementation of <see cref="IContentCandidateRepository"/> over
/// <c>social.discovered_candidates</c> (design §5.1, D6, CD-08).
/// </summary>
public sealed class EfContentCandidateRepository : IContentCandidateRepository
{
    /// <summary>
    /// The client id every CANDIDATE BANK row carries. The bank is scoped by organization + network +
    /// hashtag and shared by every client of the organization, so no client owns a row. A sentinel
    /// rather than a nullable column, so the existing unique index keeps enforcing CD-08 unchanged.
    /// </summary>
    private static readonly Guid SharedBankClientId = Guid.Empty;

    /// <summary>
    /// Upper bound on how many wordings one banked candidate stays reachable under. One niche derives
    /// at most <c>NicheHashtagDerivation.MaxHashtags</c> (5) tokens, so this leaves room for several
    /// overlapping niches without letting the set grow without limit over years of runs.
    /// </summary>
    private const int MaxBankHashtagsPerCandidate = 32;

    private readonly AppIdentityDbContext _db;

    public EfContentCandidateRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task RecordObservationsAsync(
        Guid organizationId,
        Guid clientId,
        IReadOnlyList<CandidateObservation> observations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (observations.Count == 0)
        {
            return;
        }

        // CD-08: a double-write of the SAME candidate at the SAME observed_at inside ONE call is
        // rejected — not silently duplicated. Mirrors the unique index (organization_id, client_id,
        // candidate_key, observed_at) that also protects this at the database layer in production.
        var deduped = observations
            .GroupBy(o => (o.CandidateKey, o.ObservedAtUtc))
            .Select(g => g.First());

        foreach (var observation in deduped)
        {
            _db.DiscoveredCandidates.Add(Map(organizationId, clientId, observation));
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CandidateObservation>> GetLatestByKeysAsync(
        Guid organizationId,
        Guid clientId,
        IReadOnlyList<string> candidateKeys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidateKeys);

        if (candidateKeys.Count == 0)
        {
            return [];
        }

        var rows = await _db.DiscoveredCandidates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                        && x.ClientId == clientId
                        && candidateKeys.Contains(x.CandidateKey))
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.CandidateKey)
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .Select(Map)
            .ToList();
    }

    public async Task<IReadOnlyList<CandidateObservation>> GetNicheBankAsync(
        Guid organizationId,
        string networkKey,
        IReadOnlyList<string> hashtags,
        int maxResults,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(networkKey);
        ArgumentNullException.ThrowIfNull(hashtags);

        if (hashtags.Count == 0 || maxResults <= 0)
        {
            return [];
        }

        var normalizedNetwork = Normalize(networkKey);

        // The SQL filter narrows to ONE organization's reserve for ONE network — that is the part the
        // ix_discovered_candidates_bank index serves. The hashtag overlap is then applied in memory:
        // a set-intersection predicate over a collection column has no LINQ translation shared by the
        // Postgres provider used in production and the in-memory provider the tests run under, and a
        // provider-specific expression here would be an Infrastructure detail that silently changes
        // behaviour between the two.
        var rows = await _db.DiscoveredCandidates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                        && x.ClientId == SharedBankClientId
                        && x.NetworkKey == normalizedNetwork)
            .ToListAsync(ct);

        var wanted = new HashSet<string>(hashtags.Select(Normalize), StringComparer.Ordinal);

        return rows
            .Where(row => row.BankHashtags is not null && row.BankHashtags.Any(wanted.Contains))
            .OrderByDescending(row => row.Views ?? -1)
            .ThenBy(row => row.CandidateKey, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(Map)
            .ToList();
    }

    public async Task<int> RecordNicheBankAsync(
        Guid organizationId,
        string networkKey,
        IReadOnlyList<string> hashtags,
        IReadOnlyList<CandidateObservation> observations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(networkKey);
        ArgumentNullException.ThrowIfNull(hashtags);
        ArgumentNullException.ThrowIfNull(observations);

        if (hashtags.Count == 0 || observations.Count == 0)
        {
            return 0;
        }

        var normalizedNetwork = Normalize(networkKey);
        var normalizedHashtags = hashtags.Select(Normalize).Where(h => h.Length > 0).Distinct(StringComparer.Ordinal).ToList();

        if (normalizedHashtags.Count == 0)
        {
            return 0;
        }

        // The same candidate returned twice by one run is ONE candidate — the same CandidateIdentity
        // dedup the chain applies in-run, applied again here so a provider quirk cannot double a row.
        var incoming = observations
            .GroupBy(observation => observation.CandidateKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var incomingKeys = incoming.Select(observation => observation.CandidateKey).ToList();

        var existing = await _db.DiscoveredCandidates
            .Where(x => x.OrganizationId == organizationId
                        && x.ClientId == SharedBankClientId
                        && x.NetworkKey == normalizedNetwork
                        && incomingKeys.Contains(x.CandidateKey))
            .ToListAsync(ct);

        var storedByKey = existing
            .GroupBy(row => row.CandidateKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var banked = 0;

        foreach (var observation in incoming)
        {
            if (storedByKey.TryGetValue(observation.CandidateKey, out var stored))
            {
                // Already paid for. Keep the original snapshot — rewriting it would make the age the
                // operator is shown a lie — and only widen the wordings it is reachable under.
                stored.BankHashtags = WidenHashtags(stored.BankHashtags, normalizedHashtags);
                continue;
            }

            var row = Map(organizationId, SharedBankClientId, observation);
            row.NetworkKey = normalizedNetwork;
            row.BankHashtags = normalizedHashtags.Take(MaxBankHashtagsPerCandidate).ToList();

            _db.DiscoveredCandidates.Add(row);
            banked++;
        }

        await _db.SaveChangesAsync(ct);

        return banked;
    }

    private static List<string> WidenHashtags(List<string>? current, IReadOnlyList<string> additional)
    {
        var widened = new List<string>(current ?? []);

        foreach (var hashtag in additional)
        {
            if (widened.Count >= MaxBankHashtagsPerCandidate)
            {
                break;
            }

            if (!widened.Contains(hashtag, StringComparer.Ordinal))
            {
                widened.Add(hashtag);
            }
        }

        return widened;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static DiscoveredCandidateData Map(Guid organizationId, Guid clientId, CandidateObservation o) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        ClientId = clientId,
        CandidateKey = o.CandidateKey,
        NetworkKey = o.NetworkKey,
        SourceKey = o.SourceKey,
        SearchCriterion = o.SearchCriterion,
        ContentUrl = o.ContentUrl,
        AuthorHandle = o.AuthorHandle,
        Title = o.Title,
        Summary = o.Summary,
        ThumbnailUrl = o.ThumbnailUrl,
        PublishedAt = o.PublishedAtUtc?.UtcDateTime,
        Views = o.Views,
        Likes = o.Likes,
        Comments = o.Comments,
        Shares = o.Shares,
        Saves = o.Saves,
        OpportunityScore = o.OpportunityScore,
        Classification = o.Classification,
        ObservedAt = o.ObservedAtUtc.UtcDateTime,
        CreatedAt = DateTime.UtcNow,
    };

    private static CandidateObservation Map(DiscoveredCandidateData e) => new(
        CandidateKey: e.CandidateKey,
        NetworkKey: e.NetworkKey,
        SourceKey: e.SourceKey,
        SearchCriterion: e.SearchCriterion,
        ContentUrl: e.ContentUrl,
        AuthorHandle: e.AuthorHandle,
        Title: e.Title,
        Summary: e.Summary,
        ThumbnailUrl: e.ThumbnailUrl,
        PublishedAtUtc: e.PublishedAt is null ? null : new DateTimeOffset(e.PublishedAt.Value, TimeSpan.Zero),
        Views: e.Views,
        Likes: e.Likes,
        Comments: e.Comments,
        Shares: e.Shares,
        Saves: e.Saves,
        OpportunityScore: e.OpportunityScore,
        Classification: e.Classification,
        ObservedAtUtc: new DateTimeOffset(e.ObservedAt, TimeSpan.Zero));
}
