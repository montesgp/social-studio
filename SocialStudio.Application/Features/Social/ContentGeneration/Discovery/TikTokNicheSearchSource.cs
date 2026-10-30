using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Domain.Social.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// Discovers real, high-performing TikTok content in the client's niche (CD-01 niche routing,
/// INT-DISC-01..04). <c>Order: 3</c>, <c>Supports("niche")</c> only.
///
/// THE BANK COMES FIRST. Every Discover used to dispatch a paid Apify run and throw the result away —
/// five separate runs bought data the system already held in hand. This source now reads the stored
/// candidate bank before anything else, and a billed run can happen for exactly TWO reasons:
///
/// 1. the bank has nothing for this organization + network + derived hashtags, or
/// 2. the operator explicitly asked to refresh (<see cref="ContentDiscoveryRequest.ForceRefresh"/>).
///
/// There is no time-based expiry and no automatic re-scrape on a thin pool: refresh is MANUAL ONLY.
/// The resolver is reachable through exactly one private method, so no future edit can bypass the bank
/// read by accident.
///
/// This source authors NO Apify/HTTP concerns itself — <see cref="INicheSearchResolver"/> owns actor
/// resolution (exclusively configuration-driven, CD-12/D13) and the search dispatch; this class only
/// decides WHAT to search for (the client's <see cref="Client.SectorOrNiche"/>), whether to search at
/// all, and what to bank afterwards.
/// </summary>
public sealed class TikTokNicheSearchSource : IContentDiscoverySource
{
    private const string TikTokNetwork = "tiktok";
    private const string SourceKeyValue = "niche";

    private readonly ISocialClientRepository _clients;
    private readonly INicheSearchResolver _resolver;
    private readonly IContentCandidateRepository _bank;
    private readonly IClock _clock;
    private readonly IClientNicheHashtagProvider _nicheHashtags;
    private readonly ILogger<TikTokNicheSearchSource> _logger;

    public TikTokNicheSearchSource(
        ISocialClientRepository clients,
        INicheSearchResolver resolver,
        IContentCandidateRepository bank,
        IClock clock,
        IClientNicheHashtagProvider nicheHashtags,
        ILogger<TikTokNicheSearchSource>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(nicheHashtags);

        _clients = clients;
        _resolver = resolver;
        _bank = bank;
        _clock = clock;
        _nicheHashtags = nicheHashtags;
        _logger = logger ?? NullLogger<TikTokNicheSearchSource>.Instance;
    }

    public string SourceKey => SourceKeyValue;

    public int Order => 3;

    public bool Supports(string searchCriterion) =>
        string.Equals(searchCriterion, SourceKeyValue, StringComparison.OrdinalIgnoreCase);

    public async Task<SourceDiscoveryResult> DiscoverAsync(
        ContentDiscoveryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxResults <= 0)
        {
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        // Only TikTok has a working niche-search adapter today; filtering here (rather than at the
        // caller) keeps the caller free to tick networks that are not wired up yet without an error.
        var wantsTikTok = request.NetworkSources.Count == 0
            || request.NetworkSources.Any(n => string.Equals(n, TikTokNetwork, StringComparison.OrdinalIgnoreCase));

        if (!wantsTikTok)
        {
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        var clientId = new ClientId(request.ClientId);

        var client = await _clients.GetByIdAsync(clientId, request.OrganizationId, ct);
        if (client is null)
        {
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        // THE CURATED CATALOG DECIDES. A niche's hashtags are chosen by a sysadmin and shared by every
        // client of that niche, so two clients in the same niche converge on the same search — and the
        // same bank key — by construction rather than by however their sector happens to be worded.
        var curated = await _nicheHashtags.GetHashtagsAsync(clientId, ct);

        switch (curated.Outcome)
        {
            case ClientNicheHashtagOutcome.Curated:
                _logger.LogInformation(
                    "Niche search is using the CURATED hashtags of client {ClientId}'s catalog niches: [{Hashtags}].",
                    clientId.Value,
                    string.Join(", ", curated.Hashtags));

                // The search term travels alongside for the keyword-scraper input shape and for the log;
                // it is built from the curated tags so both shapes ask for the same thing.
                return await ServeFromBankOrRunAsync(
                    request,
                    string.Join(" ", curated.Hashtags),
                    curated.Hashtags,
                    ct);

            case ClientNicheHashtagOutcome.MissingCuratedHashtags:
                // The client IS assigned to niches and NONE of them is curated. Falling back to the free
                // text would quietly resurrect the guessed hashtags the catalog exists to replace, so
                // this reports an unconfigured stage and sends the operator to the catalog instead.
                _logger.LogWarning(
                    "Niche search is UNCONFIGURED for client {ClientId}: every catalog niche assigned to it " +
                    "has an empty curated hashtag list. Curate those niches — no provider run was dispatched.",
                    clientId.Value);

                return new SourceDiscoveryResult(SourceOutcome.Unconfigured, []);
        }

        if (string.IsNullOrWhiteSpace(client.SectorOrNiche))
        {
            // No catalog niche AND no free text: nothing to search on is a legitimate empty run, not a
            // configuration problem — the niche stage may be fully configured and the client simply has
            // neither a niche assigned nor a sector set yet.
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        // LEGACY FALLBACK, for every client that predates the catalog. The SAME derivation the resolver
        // uses to build its provider input is the bank's key, so the reserve is filed under exactly the
        // tokens it was searched with. They are already normalized (lowercased, de-accented, connector
        // words dropped), which is what makes "Medical & health" and a differently-worded equivalent
        // converge where they genuinely overlap.
        var hashtags = NicheHashtagDerivation.Derive(client.SectorOrNiche);

        return await ServeFromBankOrRunAsync(request, client.SectorOrNiche, hashtags, ct);
    }

    /// <summary>
    /// The ONLY path to the paid resolver. Bank read first, unconditionally, unless the operator opted
    /// in to a refresh — expressed as one method so the ordering cannot be reshuffled by accident.
    /// </summary>
    private async Task<SourceDiscoveryResult> ServeFromBankOrRunAsync(
        ContentDiscoveryRequest request,
        string searchTerm,
        IReadOnlyList<string> hashtags,
        CancellationToken ct)
    {
        // An unrunnable derivation has no bank key either. Fall through to the resolver, which owns
        // reporting that the term yields no searchable hashtag — it never dispatches an actor for it.
        var isBankable = hashtags.Count > 0;

        if (isBankable && !request.ForceRefresh)
        {
            var banked = await _bank.GetNicheBankAsync(
                request.OrganizationId,
                TikTokNetwork,
                hashtags,
                request.MaxResults,
                ct);

            if (banked.Count > 0)
            {
                _logger.LogInformation(
                    "Niche search served {CandidateCount} candidate(s) from the stored bank for organization " +
                    "{OrganizationId} on {NetworkKey} under [{Hashtags}] — NO provider run was dispatched.",
                    banked.Count,
                    request.OrganizationId,
                    TikTokNetwork,
                    string.Join(", ", hashtags));

                return new SourceDiscoveryResult(
                    SourceOutcome.Ran,
                    banked.Select(ToDiscoveredContent).ToList(),
                    DiscoveryOrigin.Bank,
                    banked.Min(observation => observation.ObservedAtUtc));
            }
        }

        _logger.LogInformation(
            "Niche search is dispatching a BILLED provider run for organization {OrganizationId} on " +
            "{NetworkKey}. Reason={Reason}.",
            request.OrganizationId,
            TikTokNetwork,
            request.ForceRefresh ? "operator requested a refresh" : "the candidate bank is empty for this niche");

        var resolution = await _resolver.SearchAsync(
            new NicheSearchRequest(request.OrganizationId, TikTokNetwork, searchTerm, request.MaxResults)
            {
                // The hashtags the bank is keyed by ARE the hashtags the provider is asked for. Handing
                // them over (instead of letting the resolver re-derive them) is what keeps the two from
                // ever disagreeing — and what lets a curated list travel untouched.
                Hashtags = hashtags,
            },
            ct);

        if (isBankable && resolution.Outcome == SourceOutcome.Ran && resolution.Candidates.Count > 0)
        {
            await BankAsync(request.OrganizationId, hashtags, request.SearchCriterion, resolution.Candidates, ct);
        }

        return new SourceDiscoveryResult(resolution.Outcome, resolution.Candidates);
    }

    /// <summary>
    /// Banks what the run just paid for. A bank failure is logged and swallowed on purpose: losing
    /// candidates the operator already paid for because a write failed would be strictly worse than
    /// having to pay for them again next time.
    /// </summary>
    private async Task BankAsync(
        Guid organizationId,
        IReadOnlyList<string> hashtags,
        string searchCriterion,
        IReadOnlyList<DiscoveredContent> candidates,
        CancellationToken ct)
    {
        var observedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc));

        var observations = candidates
            .Select(candidate => new CandidateObservation(
                // The SAME identity the chain dedups with in-run, so the two can never disagree (D3).
                CandidateKey: CandidateIdentity.From(candidate.NetworkSource, candidate.ContentUrl),
                NetworkKey: TikTokNetwork,
                SourceKey: SourceKeyValue,
                SearchCriterion: searchCriterion,
                ContentUrl: candidate.ContentUrl,
                AuthorHandle: candidate.SourceHandle,
                Title: candidate.Title,
                Summary: candidate.Summary,
                ThumbnailUrl: candidate.ThumbnailUrl,
                PublishedAtUtc: candidate.PublishedAtUtc,
                Views: candidate.Views,
                Likes: candidate.Likes,
                Comments: candidate.Comments,
                Shares: candidate.Shares,
                Saves: candidate.Saves,
                // Scoring/classification happen further down the chain; the bank stores the observation
                // as it arrived rather than inventing a score the run never computed.
                OpportunityScore: null,
                Classification: null,
                ObservedAtUtc: observedAtUtc))
            .ToList();

        try
        {
            var banked = await _bank.RecordNicheBankAsync(organizationId, TikTokNetwork, hashtags, observations, ct);

            _logger.LogInformation(
                "Niche search banked {BankedCount} new candidate(s) of {TotalCount} for organization " +
                "{OrganizationId} on {NetworkKey} under [{Hashtags}].",
                banked,
                observations.Count,
                organizationId,
                TikTokNetwork,
                string.Join(", ", hashtags));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Niche search could not bank {CandidateCount} candidate(s) for organization {OrganizationId}. " +
                "The run's candidates are still returned; they will simply have to be scraped again later.",
                observations.Count,
                organizationId);
        }
    }

    private static DiscoveredContent ToDiscoveredContent(CandidateObservation observation) =>
        new(
            Title: observation.Title,
            Summary: observation.Summary ?? string.Empty,
            NetworkSource: observation.NetworkKey,
            ContentUrl: observation.ContentUrl,
            ThumbnailUrl: observation.ThumbnailUrl,
            Views: observation.Views,
            Likes: observation.Likes,
            Comments: observation.Comments,
            Shares: observation.Shares,
            Saves: observation.Saves,
            PublishedAtUtc: observation.PublishedAtUtc,
            SourceHandle: observation.AuthorHandle,
            SourceKey: observation.SourceKey);
}
