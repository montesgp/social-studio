using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Domain.Social.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// Composes the two catalog ports into the ONE answer a discovery source needs: the union of the
/// curated hashtags of every niche the client is assigned to.
///
/// WHY A UNION AND NOT A PICK: a client is routinely several things at once — software AND technology
/// AND AI — and the niche search takes an array of hashtags in a single call. Picking one niche would
/// throw away most of what the operator curated; searching each niche separately would multiply the
/// billed run. The union is both faithful and cheap.
///
/// WHY IT IS CAPPED: the cap is the same <see cref="NicheHashtagDerivation.MaxHashtags"/> a free-text
/// search already honours. Each extra hashtag is extra provider work and, on usage-billed actors, extra
/// money — a client assigned to eight niches must not be able to fan one run out into an expensive
/// scrape just by ticking boxes.
///
/// This type is pure Application: it depends only on ports and on the domain entity, never on EF, HTTP
/// or a provider SDK.
/// </summary>
public sealed class ClientNicheHashtagProvider : IClientNicheHashtagProvider
{
    private readonly INicheRepository _niches;
    private readonly IClientNicheRepository _links;
    private readonly ILogger<ClientNicheHashtagProvider> _logger;

    public ClientNicheHashtagProvider(
        INicheRepository niches,
        IClientNicheRepository links,
        ILogger<ClientNicheHashtagProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(niches);
        ArgumentNullException.ThrowIfNull(links);

        _niches = niches;
        _links = links;
        _logger = logger ?? NullLogger<ClientNicheHashtagProvider>.Instance;
    }

    public async Task<ClientNicheHashtags> GetHashtagsAsync(ClientId clientId, CancellationToken ct = default)
    {
        var nicheIds = await _links.GetNicheIdsAsync(clientId, ct);
        if (nicheIds.Count == 0)
        {
            // Every client that predates the catalog lands here. This is NOT a problem to report.
            return new ClientNicheHashtags(ClientNicheHashtagOutcome.NoNichesAssigned, []);
        }

        var niches = await _niches.GetByIdsAsync(nicheIds, ct);

        // Order-stable on purpose: the same assignment always produces the same hashtag list, which is
        // what makes the candidate bank key reproducible from one run to the next.
        var union = niches
            .SelectMany(niche => niche.SearchHashtags)
            .Where(hashtag => !string.IsNullOrWhiteSpace(hashtag))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (union.Count == 0)
        {
            _logger.LogWarning(
                "Client {ClientId} is assigned to {NicheCount} niche(s), but NOT ONE of them has a curated " +
                "search hashtag. The niche search cannot run: curate the hashtags of those catalog entries " +
                "(niche codes: {NicheCodes}).",
                clientId.Value,
                nicheIds.Count,
                string.Join(", ", niches.Select(niche => niche.Code)));

            return new ClientNicheHashtags(ClientNicheHashtagOutcome.MissingCuratedHashtags, []);
        }

        if (union.Count > NicheHashtagDerivation.MaxHashtags)
        {
            var kept = union.Take(NicheHashtagDerivation.MaxHashtags).ToList();

            _logger.LogWarning(
                "Client {ClientId} is assigned to niches carrying {HashtagCount} curated hashtag(s); only the " +
                "first {MaxHashtags} are searched ([{Kept}]) — the rest are dropped ([{Dropped}]) so one run " +
                "cannot fan out into an unbounded, billed scrape. Reduce the client's niches to change which " +
                "hashtags are searched.",
                clientId.Value,
                union.Count,
                NicheHashtagDerivation.MaxHashtags,
                string.Join(", ", kept),
                string.Join(", ", union.Skip(NicheHashtagDerivation.MaxHashtags)));

            return new ClientNicheHashtags(ClientNicheHashtagOutcome.Curated, kept);
        }

        return new ClientNicheHashtags(ClientNicheHashtagOutcome.Curated, union);
    }
}
