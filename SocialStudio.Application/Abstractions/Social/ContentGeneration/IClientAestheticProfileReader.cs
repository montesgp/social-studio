using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for reading the full aesthetic profile of a client from the most recent
/// completed profile analysis. Widens the former <c>IClientAestheticDescriptorReader</c>
/// (which returned only descriptor strings) to return the richer <see cref="ClientAestheticProfile"/>
/// value object (OQ-6: widen vs sibling — single caller, no ceremony).
///
/// <para>
/// CONTRACT (CAG-CG-05, PA-SEAM-01, PA-SEAM-03):
/// <list type="bullet">
/// <item>No Apify call — this is a read-only adapter over already-stored analysis data.</item>
/// <item>Returns <c>null</c> when no completed (Analyzed) analysis exists for the client.</item>
/// <item>No Infrastructure types in signature; the adapter lives in Infrastructure.</item>
/// <item>Replaces <see cref="IClientAestheticDescriptorReader"/> — that interface is retained
///     only to keep existing DI registrations and tests compiling during the transition.</item>
/// </list>
/// </para>
///
/// Refs: design §3.2 (OQ-6); CAG-CG-05; PA-SEAM-01, PA-SEAM-03.
/// </summary>
public interface IClientAestheticProfileReader
{
    /// <summary>
    /// Returns the full aesthetic profile for the given client from the most recent
    /// completed analysis record.
    /// Returns <c>null</c> when no completed (<c>Status == "Analyzed"</c>) record exists.
    /// </summary>
    /// <param name="orgId">The organization that owns the client.</param>
    /// <param name="clientId">The client whose profile analysis is queried.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct);
}
