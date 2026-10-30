using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Ideas;

/// <summary>
/// What the chain managed to gather, and what it could not.
/// </summary>
/// <param name="Ideas">The ideas to build pieces from, at most as many as were asked for.</param>
/// <param name="Shortfall">
/// How many the chain could not fill. Surfaced rather than swallowed: an operator who ordered ten
/// pieces and is getting six deserves to know before the run, not by counting the bank afterwards.
/// </param>
/// <param name="FailedSourceKeys">Sources that errored. The batch still runs; the failure is reported.</param>
public sealed record ContentIdeaPlan(
    IReadOnlyList<ContentIdea> Ideas,
    int Shortfall,
    IReadOnlyList<string> FailedSourceKeys);

/// <summary>
/// Asks each idea source, in preference order, for whatever the batch is still missing.
///
/// This is the entire "hybrid, but dynamic" rule and it needs no modes: a source that cannot serve
/// this client returns nothing and the remainder falls through to the next link. A client with a rich
/// strategy is served mostly from it; a client with none ends up purely referential — the same code
/// path, decided by the data rather than by a switch the operator has to remember to flip.
///
/// Adding Google Trends, or a second AI agent, is a new <see cref="IContentIdeaSource"/> registration.
/// Nothing here changes.
/// </summary>
public sealed class ContentIdeaChain
{
    private readonly IReadOnlyList<IContentIdeaSource> _sources;
    private readonly ILogger<ContentIdeaChain> _logger;

    public ContentIdeaChain(
        IEnumerable<IContentIdeaSource> sources,
        ILogger<ContentIdeaChain>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // Ordered here, once, so DI registration order can never decide preference.
        _sources = sources.OrderBy(source => source.Order).ThenBy(source => source.SourceKey).ToList();
        _logger = logger ?? NullLogger<ContentIdeaChain>.Instance;
    }

    public async Task<ContentIdeaPlan> DrawAsync(ContentIdeaRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Count <= 0)
        {
            return new ContentIdeaPlan([], 0, []);
        }

        var collected = new List<ContentIdea>(request.Count);
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();

        // An origin filter EXCLUDES sources rather than deprioritising them. Asking an excluded source
        // and discarding its answer would still start (and bill) the provider run the operator chose to
        // avoid, and a batch that cannot be filled from the allowed sources reports a shortfall instead
        // of silently falling back to the ones that were ruled out.
        var eligible = request.OriginFilter is { } origin
            ? _sources.Where(source => source.Origin == origin).ToList()
            : _sources;

        foreach (var source in eligible)
        {
            var remaining = request.Count - collected.Count;

            if (remaining <= 0)
            {
                break;
            }

            IReadOnlyList<ContentIdea> drawn;

            try
            {
                // Ask only for what is missing: every source downstream costs money per item, and a
                // scraper asked for ten when two are needed bills for ten.
                drawn = await source.DrawAsync(request with { Count = remaining }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A provider outage degrades the batch; it does not cancel it. The operator gets fewer
                // pieces and an explicit reason instead of a failed run.
                _logger.LogError(ex, "Content idea source {SourceKey} failed; continuing the chain.", source.SourceKey);
                failed.Add(source.SourceKey);
                continue;
            }

            if (drawn.Count == 0)
            {
                continue;
            }

            // Strongest first, so trimming to the remainder keeps the winners rather than whatever the
            // provider happened to return first. Ideas without a signal (a strategy topic has no view
            // count) sort last among their own source but are never discarded for lacking one.
            var ranked = drawn
                .OrderByDescending(idea => idea.Signal ?? double.NegativeInfinity)
                .ToList();

            foreach (var idea in ranked)
            {
                if (collected.Count >= request.Count)
                {
                    break;
                }

                // Two sources landing on the same topic would otherwise produce two near-identical
                // pieces — paid for twice, and a chore to triage in the bank.
                if (!seenTitles.Add(NormalizeTitle(idea.Title)))
                {
                    continue;
                }

                collected.Add(idea);
            }
        }

        return new ContentIdeaPlan(collected, request.Count - collected.Count, failed);
    }

    private static string NormalizeTitle(string title) => title.Trim();
}
