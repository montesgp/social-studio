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

/// <summary>How many ideas the batch still needs, and for whom.</summary>
public sealed record ContentIdeaRequest(
    Guid OrganizationId,
    Guid ClientId,
    int Count);

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
    /// Draws up to <see cref="ContentIdeaRequest.Count"/> ideas. Returning fewer — including none — is
    /// normal and simply passes the remainder to the next source.
    /// </summary>
    Task<IReadOnlyList<ContentIdea>> DrawAsync(ContentIdeaRequest request, CancellationToken ct);
}
