using SocialStudio.Application.Features.Social.Niches;

namespace SocialStudio.Application.Features.Social.Clients.Niches;

/// <summary>
/// A client's current selection from the niche catalog, ordered by display name.
/// </summary>
public sealed record ClientNichesResponse(
    Guid ClientId,
    IReadOnlyList<NicheResponse> Niches);
