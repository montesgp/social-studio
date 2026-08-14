using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Analytics;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.MarketComparison;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Semantics;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Domain.Social.NicheDiscovery.Taxonomy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// Runs one <see cref="DiscoveryRun"/> end to end (design §1 pipeline diagram, ND-02, ND-03): sourcing
/// (<see cref="NicheDiscoverySourceChain"/>, 4 stages) → deterministic analytics
/// (<see cref="DeterministicMetricCalculator"/>, per cluster) → semantics
/// (<see cref="SemanticAnnotator"/>, labels only) → market comparison
/// (<see cref="TargetMarketEvaluator"/>) → scoring (<see cref="NicheOpportunityScorer"/> +
/// <see cref="ConfidenceScorer"/>) → append-only persistence → terminal status transition.
///
/// EVIDENCE → METRIC → SIGNAL/SCORE stays in that order: semantics contributes ONLY labels
/// (<see cref="ClusterProposal.ClusterLabel"/>, <see cref="ClusterProposal.PatternLabel"/>, topic and
/// format-trait labels) — every number an opportunity is scored on comes from
/// <see cref="DeterministicMetricCalculator"/> or <see cref="TargetMarketEvaluator"/>, never from the
/// AI response. A degraded semantic layer (<see cref="LexicalClusterFallback"/> engaged) completes the
/// run as <see cref="DiscoveryRunStatus.PartiallyCompleted"/>, never <see cref="DiscoveryRunStatus.Completed"/>
/// — degradation is observable, not silently absorbed (N11).
///
/// AUTHORED, NOT LITERAL FROM DESIGN — flagged explicitly (design leaves the exact per-cluster
/// reduction unspecified; WU-9's own class docs confirm the Signal/SignalBuilder layer referenced in
/// design §3 was never scheduled by any work unit, so this processor goes straight from per-cluster
/// Metric values to <see cref="OpportunityInput"/>, the same shortcut WU-8/WU-9 already took):
/// <list type="bullet">
/// <item><description><see cref="OpportunityInput.ViralPerformance"/> = the average of the cluster's
/// non-null per-channel <c>OutlierDensity</c> values (design §7.1 names the metric, not the per-cluster
/// reduction across possibly several channels).</description></item>
/// <item><description><see cref="OpportunityInput.MonetizationPotential"/> reuses
/// <see cref="TargetMarketEvaluation.TargetMarketCompetitionValue"/> — design §7.1 describes its source
/// as "channel subscriber/view deciles in target.Language", which is EXACTLY
/// <see cref="TargetMarketCompetition"/>'s own already-computed formula; a second, parallel decile
/// computation would duplicate it for no semantic difference this MVP can articulate.</description></item>
/// <item><description><see cref="OpportunityInput.AutomationSuitability"/> is computed ONCE PER RUN
/// (not per cluster) from <see cref="SemanticAnnotation.FormatTraitLabels"/>, because WU-10 built that
/// list as run-flat, not cluster-scoped. A closed, authored weight table maps each recognized
/// <see cref="FormatTrait"/> to a 0..1 suitability score; an unrecognized/empty trait list — always the
/// case when the semantic layer degraded — yields <c>null</c> (<c>NotReported</c>, exactly the design
/// §7.1 rule "NotReported if semantics degraded").</description></item>
/// </list>
/// </summary>
public sealed class NicheDiscoveryRunProcessor : INicheDiscoveryRunProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Authored automation-suitability weight table (see class doc) — closed, not AI-influenced beyond
    // which labels the semantic layer proposed.
    private static readonly IReadOnlyDictionary<FormatTrait, double> AutomationWeightByTrait =
        new Dictionary<FormatTrait, double>
        {
            [FormatTrait.ScreenRecording] = 1.0,
            [FormatTrait.VoiceoverOverStockFootage] = 0.85,
            [FormatTrait.TextOverlayOnly] = 0.85,
            [FormatTrait.Animation] = 0.6,
            [FormatTrait.TalkingHead] = 0.25,
            [FormatTrait.LiveActionOriginalFootage] = 0.1,
        };

    private readonly IDiscoveryRunRepository _runRepository;
    private readonly NicheDiscoverySourceChain _chain;
    private readonly IAiCompletionGateway _ai;
    private readonly INicheOpportunityRepository _opportunityRepository;
    private readonly INicheDiscoveryEvidenceRepository _evidenceRepository;
    private readonly IClock _clock;
    private readonly ILogger<NicheDiscoveryRunProcessor> _logger;

    public NicheDiscoveryRunProcessor(
        IDiscoveryRunRepository runRepository,
        IEnumerable<INicheDiscoverySource> sources,
        IAiCompletionGateway ai,
        INicheOpportunityRepository opportunityRepository,
        INicheDiscoveryEvidenceRepository evidenceRepository,
        IClock clock,
        ILogger<NicheDiscoveryRunProcessor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(runRepository);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(opportunityRepository);
        ArgumentNullException.ThrowIfNull(evidenceRepository);
        ArgumentNullException.ThrowIfNull(clock);

        _runRepository = runRepository;
        _chain = new NicheDiscoverySourceChain(sources);
        _ai = ai;
        _opportunityRepository = opportunityRepository;
        _evidenceRepository = evidenceRepository;
        _clock = clock;
        _logger = logger ?? NullLogger<NicheDiscoveryRunProcessor>.Instance;
    }

    public async Task RunAsync(Guid organizationId, DiscoveryRunId runId, CancellationToken ct = default)
    {
        var run = await _runRepository.GetByIdAsync(organizationId, runId, ct);
        if (run is null)
        {
            _logger.LogWarning("NicheDiscovery run {RunId} not found for org {OrganizationId}; skipping.", runId.Value, organizationId);
            return;
        }

        if (run.Status != DiscoveryRunStatus.Requested)
        {
            _logger.LogWarning("NicheDiscovery run {RunId} is not Requested (status={Status}); skipping duplicate processing.", runId.Value, run.Status);
            return;
        }

        var nowUtc = _clock.UtcNow;
        var nowOffset = new DateTimeOffset(nowUtc, TimeSpan.Zero);

        run.Start(nowUtc);
        await _runRepository.UpdateAsync(run, ct);

        var snapshot = DiscoveryRunConfigurationSnapshot.FromJson(run.ConfigurationSnapshotJson);
        var weights = snapshot.Weights ?? OpportunityScoreWeights.Default;
        var budget = DiscoveryBudget.ForDepth(run.Depth);
        var ledger = new RunDiscoveryBudgetLedger(budget);

        var (evidence, unconfiguredKeys, failedKeys, ranKeys, failureReasonKeys) =
            await SourceAsync(run, snapshot.SeedTerms, ledger, ct);

        var providerUnitsSpent = budget.ProviderUnits - ledger.RemainingProviderUnits;

        if (evidence.Videos.Count == 0)
        {
            await FinishEmptyAsync(run, nowUtc, ranKeys, unconfiguredKeys, failedKeys, failureReasonKeys, weights, providerUnitsSpent, ct);
            return;
        }

        var candidates = evidence.Videos
            .Select(v => new SemanticAnnotationCandidate(v.ExternalRef, v.ChannelExternalRef, v.Title, v.Description))
            .ToList();

        // One SemanticAnnotator per run (WU-10: its within-run memoisation cache is an instance field
        // implicitly scoped to one run).
        var annotator = new SemanticAnnotator(_ai);
        var semanticOutcome = await annotator.AnnotateAsync(organizationId, candidates, ct);
        var aiCompletionsSpent = candidates.Count > 0 ? 1 : 0;

        var clusters = semanticOutcome.Annotation.Clusters;
        var automationSuitability = ComputeAutomationSuitability(semanticOutcome.Annotation.FormatTraitLabels);

        var comparisonInputs = new List<TargetMarketComparisonInput>(clusters.Count);
        var clusterMetricsByKey = new Dictionary<string, MetricSet>(StringComparer.Ordinal);
        var clusterEvidenceByKey = new Dictionary<string, EvidenceSet>(StringComparer.Ordinal);

        for (var i = 0; i < clusters.Count; i++)
        {
            var cluster = clusters[i];
            var key = $"cluster-{i}";

            var clusterVideos = evidence.Videos
                .Where(v => cluster.VideoExternalRefs.Contains(v.ExternalRef, StringComparer.Ordinal))
                .ToList();
            var clusterChannelRefs = clusterVideos.Select(v => v.ChannelExternalRef).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var clusterChannels = evidence.Channels.Where(c => clusterChannelRefs.Contains(c.ExternalRef)).ToList();
            var clusterEvidence = new EvidenceSet(clusterChannels, clusterVideos);
            var clusterMetrics = DeterministicMetricCalculator.Compute(clusterEvidence, run.Window, nowOffset);

            clusterMetricsByKey[key] = clusterMetrics;
            clusterEvidenceByKey[key] = clusterEvidence;
            comparisonInputs.Add(new TargetMarketComparisonInput(key, clusterEvidence, clusterMetrics));
        }

        var marketEvaluations = TargetMarketEvaluator.Evaluate(comparisonInputs, run.SourceMarketCode, run.TargetMarketCode)
            .ToDictionary(e => e.ClusterKey, StringComparer.Ordinal);

        var opportunityInputs = new List<OpportunityInput>(clusters.Count);
        var sampleShapes = new List<SampleShape>(clusters.Count);

        for (var i = 0; i < clusters.Count; i++)
        {
            var key = $"cluster-{i}";
            var metrics = clusterMetricsByKey[key];
            var clusterEvidence = clusterEvidenceByKey[key];
            var marketEvaluation = marketEvaluations[key];

            var viralPerformance = AverageOrNull(metrics.OutlierDensityByChannel.Values.Select(m => m.Value));

            opportunityInputs.Add(new OpportunityInput(
                ExternalRef: key,
                ViralPerformance: viralPerformance,
                CrossChannelValidation: metrics.CrossChannelRepetition.Value,
                DemandMomentum: metrics.GrowthProxy.Value,
                TargetMarketGap: marketEvaluation.TargetMarketGapValue,
                AutomationSuitability: automationSuitability,
                EvergreenPotential: metrics.EvergreenShare.Value,
                MonetizationPotential: marketEvaluation.TargetMarketCompetitionValue));

            sampleShapes.Add(BuildSampleShape(clusterEvidence, ranKeys.Count, nowUtc));
        }

        var confidenceScores = ConfidenceScorer.Score(sampleShapes);
        var confidenceByKey = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < opportunityInputs.Count; i++)
        {
            confidenceByKey[opportunityInputs[i].ExternalRef] = confidenceScores[i];
        }

        var scored = NicheOpportunityScorer.Score(opportunityInputs, weights, nowOffset);

        var opportunityRecords = new List<NicheOpportunityRecord>(scored.Count);
        var evidenceRecords = new List<NicheDiscoveryEvidenceRecord>();

        for (var i = 0; i < scored.Count; i++)
        {
            var scoredOpportunity = scored[i];
            var key = scoredOpportunity.ExternalRef;
            var clusterIndex = int.Parse(key["cluster-".Length..]);
            var cluster = clusters[clusterIndex];
            var metrics = clusterMetricsByKey[key];
            var clusterEvidence = clusterEvidenceByKey[key];
            var marketEvaluation = marketEvaluations[key];

            var opportunityId = Guid.NewGuid();

            opportunityRecords.Add(new NicheOpportunityRecord(
                Id: opportunityId,
                RunId: run.Id.Value,
                OrganizationId: organizationId,
                NicheId: run.NicheId,
                SubNicheLabel: cluster.ClusterLabel,
                ContentPattern: ContentPatternKindMapper.FromLabel(cluster.PatternLabel).ToString(),
                TopicLabels: semanticOutcome.Annotation.TopicLabels,
                OpportunityScore: scoredOpportunity.OpportunityScore,
                ConfidenceScore: confidenceByKey[key],
                EffectiveWeightDenominator: scoredOpportunity.EffectiveWeightDenominator,
                ExcludedSignalsJson: JsonSerializer.Serialize(scoredOpportunity.ExcludedSignals, JsonOptions),
                MedianViews: MedianOrNull(metrics.ChannelMedianViewsByChannel.Values.Select(m => m.Value)) is { } medianViews
                    ? (long)Math.Round(medianViews)
                    : null,
                OutlierRatio: AverageOrNull(metrics.OutlierRatioByVideo.Values.Select(m => m.Value)),
                OutlierDensity: AverageOrNull(metrics.OutlierDensityByChannel.Values.Select(m => m.Value)),
                GrowthProxy: metrics.GrowthProxy.Value,
                CrossChannelRepetition: metrics.CrossChannelRepetition.Value is { } repetition ? (int)repetition : null,
                TargetMarketDemand: marketEvaluation.TargetMarketDemandValue,
                TargetMarketCompetition: marketEvaluation.TargetMarketCompetitionValue,
                TargetMarketGap: marketEvaluation.TargetMarketGapValue,
                AutomationSuitability: automationSuitability,
                ChannelSampleSize: clusterEvidence.Channels.Count,
                VideoSampleSize: clusterEvidence.Videos.Count,
                Rank: i + 1,
                ObservedAtUtc: nowOffset));

            evidenceRecords.AddRange(clusterEvidence.Channels.Select(c => new NicheDiscoveryEvidenceRecord(
                Id: Guid.NewGuid(), RunId: run.Id.Value, OpportunityId: opportunityId, OrganizationId: organizationId,
                EvidenceKind: NicheDiscoveryEvidenceKind.Channel, ExternalRef: c.ExternalRef, ChannelHandle: c.Handle,
                Title: c.Handle, ThumbnailUrl: null, PublishedAtUtc: c.CreatedAtUtc, Views: null, Likes: null,
                Comments: null, SubscriberCount: c.SubscriberCount, TotalViews: c.TotalViews, VideoCount: c.VideoCount,
                DurationSeconds: null, LanguageCode: c.LanguageCode, SourceKey: c.SourceKey, ObservedAtUtc: nowOffset)));

            evidenceRecords.AddRange(clusterEvidence.Videos.Select(v => new NicheDiscoveryEvidenceRecord(
                Id: Guid.NewGuid(), RunId: run.Id.Value, OpportunityId: opportunityId, OrganizationId: organizationId,
                EvidenceKind: NicheDiscoveryEvidenceKind.Video, ExternalRef: v.ExternalRef, ChannelHandle: v.ChannelExternalRef,
                Title: v.Title, ThumbnailUrl: null, PublishedAtUtc: v.PublishedAtUtc, Views: v.Views, Likes: v.Likes,
                Comments: v.Comments, SubscriberCount: null, TotalViews: null, VideoCount: null,
                DurationSeconds: v.DurationSeconds, LanguageCode: v.LanguageCode, SourceKey: v.SourceKey, ObservedAtUtc: nowOffset)));
        }

        if (opportunityRecords.Count > 0)
        {
            await _opportunityRepository.AppendAsync(opportunityRecords, ct);
            await _evidenceRepository.AppendAsync(evidenceRecords, ct);
        }

        run.RecordExecutionSnapshot(
            weightsJson: JsonSerializer.Serialize(weights, JsonOptions),
            unconfiguredSourceKeys: unconfiguredKeys,
            failedSourceKeys: failedKeys,
            providerUnitsSpent: providerUnitsSpent,
            aiCompletionsSpent: aiCompletionsSpent,
            semanticDegraded: semanticOutcome.Degraded);

        // Observable degradation (the guiding rule for this work unit): a gateway fallback, an
        // unconfigured origin, or a failed origin all mean the run is not a clean success, even though
        // it still ranked. Completed is reserved for a run with no degradation at all.
        var degraded = semanticOutcome.Degraded || unconfiguredKeys.Count > 0 || failedKeys.Count > 0;

        if (degraded)
        {
            run.CompletePartially(nowUtc);
        }
        else
        {
            run.Complete(nowUtc);
        }

        await _runRepository.UpdateAsync(run, ct);
    }

    private async Task<(
        EvidenceSet Evidence,
        IReadOnlyList<string> UnconfiguredKeys,
        IReadOnlyList<string> FailedKeys,
        IReadOnlyList<string> RanKeys,
        IReadOnlyList<string> FailureReasonKeys)> SourceAsync(
        DiscoveryRun run, IReadOnlyList<string> seedTerms, RunDiscoveryBudgetLedger ledger, CancellationToken ct)
    {
        var channels = new List<ChannelEvidence>();
        var videos = new List<VideoEvidence>();
        var unconfigured = new HashSet<string>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var ran = new HashSet<string>(StringComparer.Ordinal);
        var failureReasonKeys = new List<string>();

        var channelSearch = await _chain.RunStageAsync(BuildQuery(run, DiscoveryStage.ChannelSearch, seedTerms, [], []), ledger, ct);
        Accumulate(channelSearch, channels, videos, unconfigured, failed, ran, failureReasonKeys);

        var videoSearch = await _chain.RunStageAsync(BuildQuery(run, DiscoveryStage.VideoSearch, seedTerms, [], []), ledger, ct);
        Accumulate(videoSearch, channels, videos, unconfigured, failed, ran, failureReasonKeys);

        var knownChannelRefs = channels.Select(c => c.ExternalRef).ToHashSet(StringComparer.Ordinal);
        var missingChannelRefs = videos
            .Select(v => v.ChannelExternalRef)
            .Distinct(StringComparer.Ordinal)
            .Where(r => !knownChannelRefs.Contains(r))
            .ToList();

        var channelDetail = await _chain.RunStageAsync(
            BuildQuery(run, DiscoveryStage.ChannelDetail, [], missingChannelRefs, []), ledger, ct);
        Accumulate(channelDetail, channels, videos, unconfigured, failed, ran, failureReasonKeys);

        // Forward-compatible no-op today: the current YouTube origin fully enriches videos inline
        // during VideoSearch, so there are no dangling video refs left to detail-enrich. A future
        // origin whose search stage returns bare refs would populate this list instead.
        var videoDetail = await _chain.RunStageAsync(
            BuildQuery(run, DiscoveryStage.VideoDetail, [], [], []), ledger, ct);
        Accumulate(videoDetail, channels, videos, unconfigured, failed, ran, failureReasonKeys);

        var dedupedChannels = channels.GroupBy(c => c.ExternalRef, StringComparer.Ordinal).Select(g => g.First()).ToList();

        return (new EvidenceSet(dedupedChannels, videos), unconfigured.ToList(), failed.ToList(), ran.ToList(), failureReasonKeys);
    }

    private static void Accumulate(
        NicheDiscoveryStageResult stage,
        List<ChannelEvidence> channels,
        List<VideoEvidence> videos,
        HashSet<string> unconfigured,
        HashSet<string> failed,
        HashSet<string> ran,
        List<string> failureReasonKeys)
    {
        channels.AddRange(stage.Channels);
        videos.AddRange(stage.Videos);
        foreach (var key in stage.UnconfiguredSourceKeys) unconfigured.Add(key);
        foreach (var key in stage.FailedSourceKeys) failed.Add(key);
        foreach (var key in stage.RanSourceKeys) ran.Add(key);
        failureReasonKeys.AddRange(stage.FailureReasonKeys);
    }

    private static NicheDiscoveryQuery BuildQuery(
        DiscoveryRun run,
        DiscoveryStage stage,
        IReadOnlyList<string> seedTerms,
        IReadOnlyList<string> channelExternalRefs,
        IReadOnlyList<string> videoExternalRefs) =>
        new(
            run.OrganizationId,
            stage,
            run.NetworkKey,
            run.ContentFormat,
            run.TargetMarketCode.Language,
            run.Window,
            seedTerms,
            channelExternalRefs,
            videoExternalRefs);

    private async Task FinishEmptyAsync(
        DiscoveryRun run,
        DateTime nowUtc,
        IReadOnlyList<string> ranKeys,
        IReadOnlyList<string> unconfiguredKeys,
        IReadOnlyList<string> failedKeys,
        IReadOnlyList<string> failureReasonKeys,
        OpportunityScoreWeights weights,
        int providerUnitsSpent,
        CancellationToken ct)
    {
        run.RecordExecutionSnapshot(
            weightsJson: JsonSerializer.Serialize(weights, JsonOptions),
            unconfiguredSourceKeys: unconfiguredKeys,
            failedSourceKeys: failedKeys,
            providerUnitsSpent: providerUnitsSpent,
            aiCompletionsSpent: 0,
            semanticDegraded: false);

        if (ranKeys.Count == 0 && unconfiguredKeys.Count > 0)
        {
            // ND-02 scenario 1: no enabled origin route resolved anywhere in the chain — distinct from
            // a source that ran cleanly and found nothing.
            run.Fail(NicheDiscoveryErrors.OriginUnconfigured.Code, nowUtc);
        }
        else if (failedKeys.Count > 0)
        {
            // ND-02 scenario 2: a depth/quota ceiling (or provider outage) was exhausted mid-run with
            // zero evidence to show for it — reported as a structured failure, never as a hollow success.
            run.Fail(failureReasonKeys.FirstOrDefault() ?? NicheDiscoveryErrors.ProviderUnavailable.Code, nowUtc);
        }
        else
        {
            // Every configured origin ran and legitimately found nothing — a success (N9/ND-02).
            run.Complete(nowUtc);
        }

        await _runRepository.UpdateAsync(run, ct);
    }

    private static double? ComputeAutomationSuitability(IReadOnlyList<string> formatTraitLabels)
    {
        var recognized = formatTraitLabels
            .Select(label => Enum.TryParse<FormatTrait>(label, ignoreCase: true, out var trait) ? trait : (FormatTrait?)null)
            .Where(trait => trait.HasValue)
            .Select(trait => AutomationWeightByTrait[trait!.Value])
            .ToList();

        return recognized.Count > 0 ? recognized.Average() : null;
    }

    private static SampleShape BuildSampleShape(EvidenceSet clusterEvidence, int sourceCount, DateTime nowUtc)
    {
        var publishedDates = clusterEvidence.Videos
            .Where(v => v.PublishedAtUtc.HasValue)
            .Select(v => v.PublishedAtUtc!.Value)
            .ToList();

        var windowCoverageDays = publishedDates.Count > 0
            ? (int)Math.Round((publishedDates.Max() - publishedDates.Min()).TotalDays)
            : 0;

        var dataAgeDays = publishedDates.Count > 0
            ? (int)Math.Round((new DateTimeOffset(nowUtc, TimeSpan.Zero) - publishedDates.Max()).TotalDays)
            : 0;

        var distinctChannelCount = clusterEvidence.Videos
            .Select(v => v.ChannelExternalRef)
            .Distinct(StringComparer.Ordinal)
            .Count();

        var signalConsistency = ComputeSignalConsistency(clusterEvidence);

        return new SampleShape(
            ChannelCount: clusterEvidence.Channels.Count,
            VideoCount: clusterEvidence.Videos.Count,
            DistinctChannelCount: distinctChannelCount,
            WindowCoverageDays: windowCoverageDays,
            SourceCount: sourceCount,
            DataAgeDays: dataAgeDays,
            SignalConsistency: signalConsistency);
    }

    /// <summary>
    /// design §7.2: <c>SignalConsistency = 1 − normalizedDispersion(per-channel OutlierRatio medians)</c>.
    /// Dispersion is the coefficient of variation of those medians, clamped to [0, 1] before inverting —
    /// fewer than two channels leaves dispersion undefined, so this is <c>null</c> (NotReported), never
    /// zero-filled.
    /// </summary>
    private static double? ComputeSignalConsistency(EvidenceSet clusterEvidence)
    {
        var metrics = DeterministicMetricCalculator.Compute(clusterEvidence, DiscoveryWindow.Default(), DateTimeOffset.UtcNow);

        var perChannelOutlierMedians = clusterEvidence.Videos
            .GroupBy(v => v.ChannelExternalRef, StringComparer.Ordinal)
            .Select(g => AverageOrNull(g
                .Select(v => metrics.OutlierRatioByVideo.TryGetValue(v.ExternalRef, out var m) ? m.Value : null)))
            .Where(m => m.HasValue)
            .Select(m => m!.Value)
            .ToList();

        if (perChannelOutlierMedians.Count < 2)
        {
            return null;
        }

        var mean = perChannelOutlierMedians.Average();
        if (mean <= 0)
        {
            return null;
        }

        var variance = perChannelOutlierMedians.Select(v => Math.Pow(v - mean, 2)).Average();
        var coefficientOfVariation = Math.Sqrt(variance) / mean;
        var normalizedDispersion = Math.Clamp(coefficientOfVariation, 0.0, 1.0);

        return 1.0 - normalizedDispersion;
    }

    private static double? AverageOrNull(IEnumerable<double?> values)
    {
        var nonNull = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return nonNull.Count > 0 ? nonNull.Average() : null;
    }

    private static double? MedianOrNull(IEnumerable<double?> values)
    {
        var sorted = values.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToList();
        if (sorted.Count == 0)
        {
            return null;
        }

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }
}
