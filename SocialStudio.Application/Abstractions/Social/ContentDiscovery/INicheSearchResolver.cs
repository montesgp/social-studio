namespace SocialStudio.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// Resolves configuration and executes a niche keyword/hashtag search, entirely behind an Apify-free
/// signature (design §2.1) — the caller never sees an actor id, a credential or a raw provider payload.
///
/// Implementations own BOTH the actor resolution (CD-12/D13 — exclusively configuration-driven, never
/// a literal id) AND the search dispatch + mapping (design §7.1 shape rules); <see cref="SearchAsync"/>
/// returns the same three-outcome shape the discovery port uses everywhere else, so a source's
/// <c>DiscoverAsync</c> can translate it into a <c>SourceDiscoveryResult</c> with no extra branching.
/// </summary>
public interface INicheSearchResolver
{
    Task<NicheSearchResolution> SearchAsync(NicheSearchRequest request, CancellationToken ct = default);
}

/// <param name="NetworkKey">Which network to search. Only <c>"tiktok"</c> has a working adapter today;
/// any other value resolves to <see cref="SourceOutcome.Unconfigured"/> — a network configured without
/// a working adapter degrades cleanly, it never crashes.</param>
/// <param name="SearchTerm">The client's niche/sector keyword (design §7 "Inputs to try").</param>
public sealed record NicheSearchRequest(
    Guid OrganizationId,
    string NetworkKey,
    string SearchTerm,
    int MaxResults)
{
    /// <summary>
    /// The hashtags to search, when the caller already knows them — the curated list of the client's
    /// catalog niches. They are used VERBATIM: re-deriving them from <see cref="SearchTerm"/> would put
    /// guessed tags back on the wire, which is the exact defect the curated catalog exists to remove.
    ///
    /// <c>null</c> or empty means "derive them from <see cref="SearchTerm"/>", the legacy behaviour for
    /// a client with no niche assigned.
    /// </summary>
    public IReadOnlyList<string>? Hashtags { get; init; }
}

/// <summary>
/// Same three-outcome shape as <see cref="SourceDiscoveryResult"/> (D12/CD-13) — the resolver IS the
/// search, so its outcome maps directly onto the owning source's per-source result.
/// </summary>
public sealed record NicheSearchResolution(
    SourceOutcome Outcome,
    IReadOnlyList<DiscoveredContent> Candidates);
