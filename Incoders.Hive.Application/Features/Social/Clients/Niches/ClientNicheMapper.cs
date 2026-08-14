using Incoders.Hive.Application.Features.Social.Niches;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Niches;

namespace Incoders.Hive.Application.Features.Social.Clients.Niches;

internal static class ClientNicheMapper
{
    /// <summary>
    /// Ordering is a contract, not a convenience: the client form renders the selection as a pill
    /// list, and repository iteration order would reshuffle it between reads.
    /// </summary>
    public static ClientNichesResponse ToResponse(ClientId clientId, IEnumerable<Niche> niches) =>
        new(
            clientId.Value,
            niches
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(NicheResponse.FromDomain)
                .ToList());
}
