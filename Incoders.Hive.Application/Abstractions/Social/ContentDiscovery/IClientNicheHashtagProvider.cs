using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// Answers ONE question for a discovery source: which hashtags does the sysadmin-curated niche catalog
/// say this client should be searched under?
///
/// WHY A PORT: <c>NicheHashtagDerivation</c> is a pure text helper and must stay pure — it takes a
/// string and returns strings. Reading a client's niche assignments needs persistence, which the pure
/// helper may never touch, so the resolution lives behind this port instead. The source depends on the
/// port; the implementation composes the two existing catalog repositories.
/// </summary>
public interface IClientNicheHashtagProvider
{
    Task<ClientNicheHashtags> GetHashtagsAsync(ClientId clientId, CancellationToken ct = default);
}

/// <summary>
/// Why the caller got what it got. The three cases are genuinely different decisions, so they are three
/// outcomes rather than "an empty list" the caller has to reinterpret.
/// </summary>
public enum ClientNicheHashtagOutcome
{
    /// <summary>
    /// The client is assigned to at least one niche and at least one of them carries curated hashtags.
    /// <see cref="ClientNicheHashtags.Hashtags"/> is non-empty and IS the search.
    /// </summary>
    Curated,

    /// <summary>
    /// The client is assigned to no catalog niche at all. Legitimate and common — every client that
    /// predates the catalog is in this state — so the caller falls back to the legacy free-text
    /// derivation over <c>Client.SectorOrNiche</c>.
    /// </summary>
    NoNichesAssigned,

    /// <summary>
    /// The client IS assigned to niches, but not one of them has a curated hashtag. That is an
    /// unfinished catalog entry, so the caller must report an unconfigured stage and send the operator
    /// to the catalog — falling back to the free text would silently re-introduce exactly the guessed
    /// hashtags the catalog exists to replace.
    /// </summary>
    MissingCuratedHashtags,
}

/// <summary>
/// The curated hashtags for one client, de-duplicated, order-stable and already capped.
/// <see cref="Hashtags"/> is non-empty only for <see cref="ClientNicheHashtagOutcome.Curated"/>.
/// </summary>
public sealed record ClientNicheHashtags(
    ClientNicheHashtagOutcome Outcome,
    IReadOnlyList<string> Hashtags);
