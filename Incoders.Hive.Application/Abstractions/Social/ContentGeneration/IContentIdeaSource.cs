namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// One idea a piece can be built from: a topic and an angle, plus where it came from.
///
/// The FORMAT is deliberately absent. What gets built (script, carousel, story) and how many comes
/// from the operator's composition cart; an idea only answers "about what".
/// </summary>
/// <param name="Title">The topic/angle, used as the piece's brief.</param>
/// <param name="Summary">Optional longer context for the generator.</param>
/// <param name="SourceKey">Which source produced it — a free string, never an enum, so a new source needs no edit here.</param>
/// <param name="Reference">Where it came from when there is a concrete origin: a post URL, an account handle, a trend key.</param>
/// <param name="Signal">
/// Ranking signal when the source has one (views today, engagement rate tomorrow). Null when the
/// source cannot rank — a strategy-derived idea has no view count and must not be treated as weak.
/// </param>
public sealed record ContentIdea(
    string Title,
    string? Summary,
    string SourceKey,
    string? Reference = null,
    double? Signal = null);

/// <summary>
/// Where an idea ultimately comes from. This is the ONE distinction the operator cares about when
/// they ask for "spontaneous ideas": not which provider produced it, but whether a real post somebody
/// else published is standing behind it.
/// </summary>
public enum IdeaOrigin
{
    /// <summary>
    /// Written by a model from what we know about the client. No reference, no anchor, no audience
    /// signal — and no provider run to pay for.
    /// </summary>
    Invented = 0,

    /// <summary>
    /// Mined from content that actually exists and actually performed. Carries a real signal, and
    /// reaching it means scraping.
    /// </summary>
    Observed = 1,
}

/// <summary>How many ideas the batch still needs, and for whom.</summary>
/// <param name="OriginFilter">
/// When set, only sources of that origin are consulted. <see cref="IdeaOrigin.Invented"/> is the
/// "ideas that do not come from anyone's winning posts" mode: excluded sources are not called at all,
/// so nothing is billed, and an unfillable batch reports a shortfall instead of quietly falling back
/// to the scrapers the operator was avoiding. Null (the default) keeps the ordinary hybrid chain.
/// </param>
/// <param name="VariationSeed">
/// Opaque per-run token mixed into the model input so two runs for the same client do not return the
/// same topics. Null for the grounded batch path, whose output must stay reproducible.
/// </param>
public sealed record ContentIdeaRequest(
    Guid OrganizationId,
    Guid ClientId,
    int Count,
    IdeaOrigin? OriginFilter = null,
    string? VariationSeed = null);

/// <summary>
/// A source of content ideas: the client's own strategy, their reference accounts, the wider market,
/// or an AI agent riffing on everything we know about the client.
///
/// THE CONTRACT THAT MAKES DEGRADATION WORK: a source that cannot serve this client right now returns
/// an EMPTY list. It does not throw and it does not fail the batch. A client with no strategy simply
/// contributes nothing at that link, and the chain moves on — which is how the hybrid plan collapses
/// into a purely referential one without anybody choosing a mode.
/// </summary>
public interface IContentIdeaSource
{
    /// <summary>Stable identifier, e.g. "client-strategy", "reference-accounts", "market-discovery", "ai-agent".</summary>
    string SourceKey { get; }

    /// <summary>Lower runs first. Preference order, not importance.</summary>
    int Order { get; }

    /// <summary>
    /// Whether this source invents topics or mines real ones. Deliberately NOT defaulted: a new source
    /// must state which side it is on, because getting it wrong would let a scraper run inside the mode
    /// whose entire purpose is to avoid one.
    /// </summary>
    IdeaOrigin Origin { get; }

    /// <summary>
    /// Draws up to <see cref="ContentIdeaRequest.Count"/> ideas. Returning fewer — including none — is
    /// normal and simply passes the remainder to the next source.
    /// </summary>
    Task<IReadOnlyList<ContentIdea>> DrawAsync(ContentIdeaRequest request, CancellationToken ct);
}
