using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// Asks each discovery source, in preference order, for whatever the shortlist is still missing.
/// The IContentDiscoveryService THE CHAIN IS. Mirrors <c>ContentIdeaChain</c> line-for-line in shape.
///
/// A source that cannot serve the requested criterion returns nothing and the remainder falls
/// through to the next link (CD-01). Adding a new discovery source is one more registration; nothing
/// here changes.
/// </summary>
public sealed class ContentDiscoveryChain : IContentDiscoveryService
{
    private readonly IReadOnlyList<IContentDiscoverySource> _sources;
    private readonly ILogger<ContentDiscoveryChain> _logger;

    public ContentDiscoveryChain(
        IEnumerable<IContentDiscoverySource> sources,
        ILogger<ContentDiscoveryChain>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // Ordered here, once, so DI registration order can never decide preference.
        _sources = sources.OrderBy(source => source.Order).ThenBy(source => source.SourceKey).ToList();
        _logger = logger ?? NullLogger<ContentDiscoveryChain>.Instance;
    }

    public async Task<ContentDiscoveryPlan> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MaxResults <= 0)
        {
            return new ContentDiscoveryPlan([], 0, [], []);
        }

        // D4: a source declares Supports(criterion); niche gets the niche source only, no filler.
        var selected = _sources.Where(source => source.Supports(request.SearchCriterion)).ToList();

        if (selected.Count == 0)
        {
            // D5: viral/competitors are valid catalog values with no dedicated source — fall back to
            // the full ordered chain rather than an error.
            selected = _sources.ToList();
        }

        var collected = new List<DiscoveredContent>(request.MaxResults);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var failed = new List<string>();
        var unconfigured = new List<string>();

        foreach (var source in selected)
        {
            var remaining = request.MaxResults - collected.Count;

            if (remaining <= 0)
            {
                break;
            }

            SourceDiscoveryResult result;

            try
            {
                // Ask only for what is missing: every source downstream costs money per item.
                result = await source.DiscoverAsync(request with { MaxResults = remaining }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A provider outage degrades the shortlist; it does not cancel the run.
                _logger.LogError(ex, "Content discovery source {SourceKey} failed; continuing the chain.", source.SourceKey);
                failed.Add(source.SourceKey);
                continue;
            }

            if (result.Outcome == SourceOutcome.Unconfigured)
            {
                // CD-13/D12: unconfigured is NOT a failure — it is a distinct, structured signal.
                unconfigured.Add(source.SourceKey);
                continue;
            }

            if (result.Outcome == SourceOutcome.Failed)
            {
                failed.Add(source.SourceKey);
                continue;
            }

            // Strongest first, so trimming to the remainder keeps the winners.
            var ranked = result.Candidates
                .OrderByDescending(candidate => candidate.Views ?? -1)
                .ToList();

            foreach (var candidate in ranked)
            {
                if (collected.Count >= request.MaxResults)
                {
                    break;
                }

                // CD-02: the same content returned by two sources is deduplicated.
                if (!seen.Add(CandidateIdentity.From(candidate.NetworkSource, candidate.ContentUrl)))
                {
                    continue;
                }

                collected.Add(candidate);
            }
        }

        return new ContentDiscoveryPlan(collected, request.MaxResults - collected.Count, failed, unconfigured);
    }
}
