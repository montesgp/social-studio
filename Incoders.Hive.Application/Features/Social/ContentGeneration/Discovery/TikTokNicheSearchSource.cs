using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// Discovers real, high-performing TikTok content in the client's niche via a configured Apify
/// niche-search actor (CD-01 niche routing, INT-DISC-01..04). <c>Order: 3</c>, <c>Supports("niche")</c>
/// only.
///
/// This source authors NO Apify/HTTP concerns itself — <see cref="INicheSearchResolver"/> owns actor
/// resolution (exclusively configuration-driven, CD-12/D13) and the search dispatch; this class only
/// decides WHAT to search for (the client's <see cref="Client.SectorOrNiche"/>) and translates the
/// resolver's outcome into the port's shared <see cref="SourceDiscoveryResult"/> shape.
/// </summary>
public sealed class TikTokNicheSearchSource : IContentDiscoverySource
{
    private const string TikTokNetwork = "tiktok";
    private const string SourceKeyValue = "niche";

    private readonly ISocialClientRepository _clients;
    private readonly INicheSearchResolver _resolver;

    public TikTokNicheSearchSource(ISocialClientRepository clients, INicheSearchResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(resolver);

        _clients = clients;
        _resolver = resolver;
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

        var client = await _clients.GetByIdAsync(new ClientId(request.ClientId), request.OrganizationId, ct);
        if (client is null || string.IsNullOrWhiteSpace(client.SectorOrNiche))
        {
            // Nothing to search on is a legitimate empty run, not a configuration problem — the niche
            // stage may be fully configured and the client simply has no sector/niche set yet.
            return new SourceDiscoveryResult(SourceOutcome.Ran, []);
        }

        var resolution = await _resolver.SearchAsync(
            new NicheSearchRequest(request.OrganizationId, TikTokNetwork, client.SectorOrNiche, request.MaxResults),
            ct);

        return new SourceDiscoveryResult(resolution.Outcome, resolution.Candidates);
    }
}
