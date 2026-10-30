using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Niches;

namespace SocialStudio.Application.Abstractions.Social;

/// <summary>
/// Persistence port for the many-to-many association between a client and catalog niches.
/// </summary>
/// <remarks>
/// This is additive: the free-text <c>Client.SectorOrNiche</c> column is untouched and keeps working.
/// The association exists so downstream keys can point at a curated catalog entry instead of at
/// however someone happened to type the sector.
/// </remarks>
public interface IClientNicheRepository
{
    Task<IReadOnlyList<NicheId>> GetNicheIdsAsync(ClientId clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the client's whole selection. An empty list clears every association.
    /// </summary>
    Task ReplaceAsync(
        ClientId clientId,
        IReadOnlyList<NicheId> nicheIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
