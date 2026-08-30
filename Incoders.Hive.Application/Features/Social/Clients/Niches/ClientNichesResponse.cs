using Incoders.Hive.Application.Features.Social.Niches;

namespace Incoders.Hive.Application.Features.Social.Clients.Niches;

/// <summary>
/// A client's current selection from the niche catalog, ordered by display name.
/// </summary>
public sealed record ClientNichesResponse(
    Guid ClientId,
    IReadOnlyList<NicheResponse> Niches);
