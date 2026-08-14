using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// One stage's evidence, plus what every source reported about itself (ND-03). Unconfigured, Failed
/// and Ran source keys are three disjoint lists — a source key never appears in more than one — which
/// is what lets a caller report "channel search: two origins ran, one was unconfigured" instead of a
/// single collapsed boolean.
/// </summary>
public sealed record NicheDiscoveryStageResult(
    IReadOnlyList<ChannelEvidence> Channels,
    IReadOnlyList<VideoEvidence> Videos,
    IReadOnlyList<string> RanSourceKeys,
    IReadOnlyList<string> UnconfiguredSourceKeys,
    IReadOnlyList<string> FailedSourceKeys,
    IReadOnlyList<string> FailureReasonKeys);

/// <summary>
/// Ordered, partial-tolerant per-stage source chain (design §5.1, ND-03). Asks every registered
/// <see cref="INicheDiscoverySource"/> that <see cref="INicheDiscoverySource.Supports"/> the requested
/// stage, in <see cref="INicheDiscoverySource.Order"/> order — DI registration order never decides.
///
/// Evidence from EVERY <c>Ran</c> source is accumulated (not "first source wins"): a channel-search
/// stage with two configured origins gets evidence from both. A source that reports
/// <c>Unconfigured</c> or <c>Failed</c> never empties the stage for a sibling source that could still
/// contribute (ND-03) — the loop always continues to the next source.
///
/// Adding a second origin (e.g. Apify) is a new DI registration; nothing here changes.
/// </summary>
public sealed class NicheDiscoverySourceChain
{
    private readonly IReadOnlyList<INicheDiscoverySource> _sources;

    public NicheDiscoverySourceChain(IEnumerable<INicheDiscoverySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources.OrderBy(s => s.Order).ThenBy(s => s.SourceKey, StringComparer.Ordinal).ToList();
    }

    public async Task<NicheDiscoveryStageResult> RunStageAsync(
        NicheDiscoveryQuery query, IDiscoveryBudgetLedger ledger, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(ledger);

        var channels = new List<ChannelEvidence>();
        var videos = new List<VideoEvidence>();
        var ran = new List<string>();
        var unconfigured = new List<string>();
        var failed = new List<string>();
        var failureReasonKeys = new List<string>();

        foreach (var source in _sources.Where(s => s.Supports(query.Stage)))
        {
            // A source with nothing left to spend is skipped, not asked and blamed for failing —
            // running out of budget mid-run is not the same failure as a provider outage.
            if (!ledger.CanAfford(0) || (ledger.RemainingProviderUnits <= 0 && ledger.RemainingAiCompletions <= 0))
            {
                break;
            }

            var budgetSnapshot = new DiscoveryBudget(ledger.RemainingProviderUnits, ledger.RemainingAiCompletions);

            NicheDiscoverySourceResult result;
            try
            {
                result = await source.DiscoverAsync(query, budgetSnapshot, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failed.Add(source.SourceKey);
                continue;
            }

            if (result.Spend.ProviderUnits > 0 || result.Spend.AiCompletions > 0)
            {
                ledger.Spend(result.Spend.ProviderUnits, result.Spend.AiCompletions);
            }

            switch (result.Outcome)
            {
                case NicheSourceOutcome.Ran:
                    channels.AddRange(result.Channels);
                    videos.AddRange(result.Videos);
                    ran.Add(source.SourceKey);
                    break;
                case NicheSourceOutcome.Unconfigured:
                    unconfigured.Add(source.SourceKey);
                    break;
                case NicheSourceOutcome.Failed:
                    failed.Add(source.SourceKey);
                    if (result.FailureReasonKey is { } reasonKey)
                    {
                        failureReasonKeys.Add(reasonKey);
                    }

                    break;
            }
        }

        return new NicheDiscoveryStageResult(channels, videos, ran, unconfigured, failed, failureReasonKeys);
    }
}
