using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using SocialStudio.Application.Features.Social.NicheDiscovery.Semantics;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Tests.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// End-to-end RED tests for <see cref="NicheDiscoveryRunProcessor"/> (task 11a.3, design §1). Composes
/// every prior work unit over fake sources/gateway/repositories — the four-layer discipline
/// (Evidence → Metric → Signal/Score, semantics contributing labels only) either holds here or it
/// quietly collapses.
/// </summary>
public sealed class NicheDiscoveryRunProcessorTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Discovery is partitioned by language, so the run itself cannot say WHERE a niche lives. The
    /// provider reports the channel's country, and the operator reviewing an opportunity by hand needs
    /// it — so it must reach the persisted evidence instead of being dropped at the boundary.
    /// </summary>
    [Fact]
    public async Task RunAsync_CarriesTheProviderReportedChannelCountryOntoChannelEvidence()
    {
        var run = CreateRun();
        var evidenceRepo = new FakeEvidenceRepository();
        var processor = BuildProcessor(
            new FakeRunRepository(run), [FakeSource.Configured("youtube-data-api", Evidence(distinctChannels: 3))],
            FakeGateway.Success(), new FakeOpportunityRepository(), evidenceRepo);

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        var channelRows = evidenceRepo.Appended
            .Where(e => e.EvidenceKind == NicheDiscoveryEvidenceKind.Channel)
            .ToList();

        Assert.NotEmpty(channelRows);
        Assert.All(channelRows, row => Assert.Equal("AR", row.Country));
    }

    /// <summary>
    /// A video row has no country of its own — the provider reports it per channel — so it inherits its
    /// OWNING channel's country rather than being left blank on a row the operator is looking at.
    /// </summary>
    [Fact]
    public async Task RunAsync_VideoEvidenceInheritsItsOwningChannelsCountry()
    {
        var run = CreateRun();
        var evidenceRepo = new FakeEvidenceRepository();
        var processor = BuildProcessor(
            new FakeRunRepository(run), [FakeSource.Configured("youtube-data-api", Evidence(distinctChannels: 3))],
            FakeGateway.Success(), new FakeOpportunityRepository(), evidenceRepo);

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        var videoRows = evidenceRepo.Appended
            .Where(e => e.EvidenceKind == NicheDiscoveryEvidenceKind.Video)
            .ToList();

        Assert.NotEmpty(videoRows);
        Assert.All(videoRows, row => Assert.Equal("AR", row.Country));
    }

    /// <summary>
    /// The discovery case: with no seed terms the processor must run the query-less sweep, NOT the
    /// search stages. Searching without a term harvests nothing, which is exactly how a run used to
    /// complete with zero evidence while looking successful.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithoutSeedTerms_RunsTheTrendingSweep_NotTheSearchStages()
    {
        var run = CreateRun(seedTerms: []);
        var source = new StageRecordingSource(Evidence(distinctChannels: 3));
        var processor = BuildProcessor(
            new FakeRunRepository(run), [source], FakeGateway.Success(),
            new FakeOpportunityRepository(), new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        Assert.Contains(DiscoveryStage.TrendingSweep, source.ObservedStages);
        Assert.DoesNotContain(DiscoveryStage.ChannelSearch, source.ObservedStages);
        Assert.DoesNotContain(DiscoveryStage.VideoSearch, source.ObservedStages);
    }

    /// <summary>Given seed terms, the operator asked a targeted question — the sweep must not run.</summary>
    [Fact]
    public async Task RunAsync_WithSeedTerms_RunsTheSearchStages_NotTheSweep()
    {
        var run = CreateRun();
        var source = new StageRecordingSource(Evidence(distinctChannels: 3));
        var processor = BuildProcessor(
            new FakeRunRepository(run), [source], FakeGateway.Success(),
            new FakeOpportunityRepository(), new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        Assert.Contains(DiscoveryStage.ChannelSearch, source.ObservedStages);
        Assert.DoesNotContain(DiscoveryStage.TrendingSweep, source.ObservedStages);
    }

    [Fact]
    public async Task RunAsync_WithoutSeedTerms_StillProducesRankedOpportunities()
    {
        var run = CreateRun(seedTerms: []);
        var opportunityRepo = new FakeOpportunityRepository();
        var processor = BuildProcessor(
            new FakeRunRepository(run), [new StageRecordingSource(Evidence(distinctChannels: 3))],
            FakeGateway.Success(), opportunityRepo, new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        Assert.NotEmpty(opportunityRepo.Appended);
    }

    /// <summary>Records which stages it was asked for, and answers every one with the same evidence.</summary>
    private sealed class StageRecordingSource : INicheDiscoverySource
    {
        private readonly EvidenceSet _evidence;

        public StageRecordingSource(EvidenceSet evidence) => _evidence = evidence;

        public List<DiscoveryStage> ObservedStages { get; } = [];

        public string SourceKey => "youtube-data-api";

        public int Order => 1;

        public bool Supports(DiscoveryStage stage) => true;

        public Task<NicheDiscoverySourceResult> DiscoverAsync(
            NicheDiscoveryQuery query, DiscoveryBudget budget, CancellationToken ct = default)
        {
            ObservedStages.Add(query.Stage);

            return Task.FromResult(query.Stage switch
            {
                DiscoveryStage.TrendingSweep or DiscoveryStage.VideoSearch =>
                    NicheDiscoverySourceResult.Ran([], _evidence.Videos, new BudgetSpend(1, 0)),
                DiscoveryStage.ChannelSearch or DiscoveryStage.ChannelDetail =>
                    NicheDiscoverySourceResult.Ran(_evidence.Channels, [], new BudgetSpend(1, 0)),
                _ => NicheDiscoverySourceResult.Ran([], [], new BudgetSpend(0, 0)),
            });
        }
    }

    /// <summary>
    /// A sweep over a broad corpus proposed 387 clusters on the first live run — a list nobody reviews is
    /// the same as no list. The cut is applied AFTER ranking, so what survives is the best, not the first
    /// the annotator happened to emit.
    /// </summary>
    [Fact]
    public async Task RunAsync_PersistsAtMostTheDepthsOpportunityCeiling()
    {
        var run = CreateRun(depth: DiscoveryDepth.Quick);
        var opportunityRepo = new FakeOpportunityRepository();
        var processor = BuildProcessor(
            new FakeRunRepository(run), [FakeSource.Configured("youtube-data-api", WideEvidence(ClusterCount))],
            FakeGateway.SuccessWithClusters(WideClusters(ClusterCount)), opportunityRepo, new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        var ceiling = DiscoveryBudget.ForDepth(DiscoveryDepth.Quick).MaxOpportunities;

        // EQUAL, not just "within": the fixture proposes ClusterCount clusters precisely so the cut has
        // to bite. An InRange here would pass even if clustering silently produced two opportunities.
        Assert.True(ClusterCount > ceiling, "Fixture must propose more clusters than the ceiling.");
        Assert.Equal(ceiling, opportunityRepo.Appended.Count);
    }

    [Fact]
    public async Task RunAsync_KeepsTheHighestScoringOpportunities_WhenTheCeilingCuts()
    {
        var run = CreateRun(depth: DiscoveryDepth.Quick);
        var opportunityRepo = new FakeOpportunityRepository();
        var processor = BuildProcessor(
            new FakeRunRepository(run), [FakeSource.Configured("youtube-data-api", WideEvidence(ClusterCount))],
            FakeGateway.SuccessWithClusters(WideClusters(ClusterCount)), opportunityRepo, new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        var scores = opportunityRepo.Appended.Select(o => o.OpportunityScore).ToList();
        Assert.Equal(scores.OrderByDescending(x => x).ToList(), scores);
        Assert.Equal(Enumerable.Range(1, scores.Count), opportunityRepo.Appended.Select(o => o.Rank));
    }

    [Fact]
    public async Task RunAsync_HappyPath_SourcesAnalysesScoresAndPersistsAppendOnly_ThenCompletes()
    {
        var run = CreateRun();
        var runRepo = new FakeRunRepository(run);
        var source = FakeSource.Configured("youtube-data-api", Evidence(distinctChannels: 3));
        var opportunityRepo = new FakeOpportunityRepository();
        var evidenceRepo = new FakeEvidenceRepository();
        var processor = BuildProcessor(runRepo, [source], FakeGateway.Success(), opportunityRepo, evidenceRepo);

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        var persisted = runRepo.Runs[run.Id];
        Assert.Equal(DiscoveryRunStatus.Completed, persisted.Status);
        Assert.NotEmpty(opportunityRepo.Appended);
        Assert.NotEmpty(evidenceRepo.Appended);
        Assert.All(opportunityRepo.Appended, o => Assert.InRange(o.OpportunityScore, 0.0, 1.0));
        Assert.All(opportunityRepo.Appended, o => Assert.InRange(o.ConfidenceScore, 0.0, 1.0));
    }

    [Fact]
    public async Task RunAsync_TransitionsThroughTheStatusLifecycle()
    {
        var run = CreateRun();
        var runRepo = new FakeRunRepository(run);
        var source = FakeSource.Configured("youtube-data-api", Evidence(distinctChannels: 3));
        var processor = BuildProcessor(runRepo, [source], FakeGateway.Success(), new FakeOpportunityRepository(), new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        Assert.Contains(DiscoveryRunStatus.Running, runRepo.ObservedStatuses);
        Assert.Equal(DiscoveryRunStatus.Completed, runRepo.ObservedStatuses[^1]);
    }

    [Fact]
    public async Task RunAsync_UnconfiguredOriginWithNoEvidenceAnywhere_FailsWithOriginUnconfigured()
    {
        var run = CreateRun();
        var runRepo = new FakeRunRepository(run);
        var source = FakeSource.Unconfigured("youtube-data-api");
        var processor = BuildProcessor(runRepo, [source], FakeGateway.Success(), new FakeOpportunityRepository(), new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        var persisted = runRepo.Runs[run.Id];
        Assert.Equal(DiscoveryRunStatus.Failed, persisted.Status);
        Assert.Equal(NicheDiscoveryErrors.OriginUnconfigured.Code, persisted.FailureReasonKey);
    }

    [Fact]
    public async Task RunAsync_QuotaExhaustedMidRunWithZeroEvidence_FailsWithAStructuredReason()
    {
        var run = CreateRun();
        var runRepo = new FakeRunRepository(run);
        var source = FakeSource.Failing("youtube-data-api", "social.niche_discovery.provider_quota_exhausted");
        var processor = BuildProcessor(runRepo, [source], FakeGateway.Success(), new FakeOpportunityRepository(), new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        var persisted = runRepo.Runs[run.Id];
        Assert.Equal(DiscoveryRunStatus.Failed, persisted.Status);
        Assert.Equal("social.niche_discovery.provider_quota_exhausted", persisted.FailureReasonKey);
    }

    [Fact]
    public async Task RunAsync_SourceRanCleanlyWithZeroEvidence_CompletesAsASuccessNotAFailure()
    {
        // ND-02 scenario 1: an unconfigured origin is reported distinctly from an empty-but-successful result.
        var run = CreateRun();
        var runRepo = new FakeRunRepository(run);
        var source = FakeSource.Configured("youtube-data-api", new EvidenceSet([], []));
        var processor = BuildProcessor(runRepo, [source], FakeGateway.Success(), new FakeOpportunityRepository(), new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        var persisted = runRepo.Runs[run.Id];
        Assert.Equal(DiscoveryRunStatus.Completed, persisted.Status);
        Assert.Null(persisted.FailureReasonKey);
    }

    [Fact]
    public async Task RunAsync_AiGatewayFails_FallsBackToLexicalClustering_AndCompletesPartiallyNeverCompleted()
    {
        var run = CreateRun();
        var runRepo = new FakeRunRepository(run);
        var source = FakeSource.Configured("youtube-data-api", Evidence(distinctChannels: 3));
        var opportunityRepo = new FakeOpportunityRepository();
        var processor = BuildProcessor(runRepo, [source], FakeGateway.Failing(), opportunityRepo, new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        var persisted = runRepo.Runs[run.Id];
        Assert.Equal(DiscoveryRunStatus.PartiallyCompleted, persisted.Status);
        Assert.True(persisted.SemanticDegraded);
        Assert.NotEmpty(opportunityRepo.Appended);
    }

    /// <summary>
    /// The ranking must come from the evidence. `InRange(0, 1)` alone is satisfied by a scorer that
    /// returns a constant, and a constant scorer leaves the pipeline looking healthy while ranking
    /// nothing — so this pins the Evidence → Metric → Score chain by putting two clusters in ONE run
    /// and requiring the one carrying a 40x outlier to outrank the flat one.
    ///
    /// Both clusters must live in the same run because <c>AvailabilityWeightedAggregate.NormalizeWithinSet</c>
    /// is set-relative min-max: a run holding a single opportunity has nothing to normalize against
    /// and neutralizes every signal to 0.5 by design, so a one-cluster fixture cannot detect a
    /// constant scorer at all.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheClusterCarryingAnOutlier_OutranksTheFlatClusterInTheSameRun()
    {
        var evidence = TwoClusterEvidence(out var flatVideoRefs, out var outlierVideoRefs);

        var opportunityRepo = new FakeOpportunityRepository();
        var run = CreateRun();
        var runRepo = new FakeRunRepository(run);
        var source = FakeSource.Configured("youtube-data-api", evidence);
        var gateway = FakeGateway.SuccessWithClusters(
            ("flat travel", flatVideoRefs),
            ("hot travel", outlierVideoRefs));
        var processor = BuildProcessor(runRepo, [source], gateway, opportunityRepo, new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        Assert.Equal(2, opportunityRepo.Appended.Count);
        // The cluster label lands on SubNicheLabel; TopicLabels is run-level and shared by both.
        var flat = opportunityRepo.Appended.Single(o => o.SubNicheLabel == "flat travel");
        var hot = opportunityRepo.Appended.Single(o => o.SubNicheLabel == "hot travel");

        Assert.True(
            hot.OpportunityScore > flat.OpportunityScore,
            $"The cluster carrying a 40x outlier scored {hot.OpportunityScore} against the flat cluster's "
                + $"{flat.OpportunityScore}. Equal scores mean the ranking is not derived from the evidence.");
    }

    /// <summary>
    /// A one-opportunity run reports a neutral 0.5, not a measurement. Pinning this documents a real
    /// product limitation rather than leaving it to be discovered from a dashboard: the score is a
    /// position within its run, so it is not comparable across runs and says nothing absolute about a
    /// niche found on its own.
    /// </summary>
    [Fact]
    public async Task RunAsync_ASingleOpportunityRun_ReportsANeutralScore_BecauseScoringIsSetRelative()
    {
        var spectacular = await ScoreOf(EvidenceWithOutlier(distinctChannels: 3, outlierMultiplier: 40));
        var unremarkable = await ScoreOf(EvidenceWithOutlier(distinctChannels: 3, outlierMultiplier: 1));

        Assert.Equal(0.5, spectacular, precision: 6);
        Assert.Equal(0.5, unremarkable, precision: 6);
    }

    /// <summary>
    /// Deterministic metrics must be computed, not defaulted. A median that ignores the fixture is a
    /// metric layer that is present in the type system and absent in behaviour.
    /// </summary>
    [Fact]
    public async Task RunAsync_ComputesMedianViewsFromTheEvidence()
    {
        var opportunityRepo = new FakeOpportunityRepository();
        await RunWith(EvidenceWithOutlier(distinctChannels: 3, outlierMultiplier: 1), opportunityRepo);

        var medians = opportunityRepo.Appended.Select(o => o.MedianViews).ToList();

        Assert.NotEmpty(medians);
        Assert.All(medians, median => Assert.NotNull(median));
        // The fixture's videos sit in the 10_000..12_000 band; a median outside it is not this evidence's.
        Assert.All(medians, median => Assert.InRange(median!.Value, 9_000, 13_000));
    }

    /// <summary>
    /// ND-13's navigable evidence: an opportunity the operator cannot drill into is an unsupported
    /// claim. Every persisted evidence row must hang off an opportunity that was actually persisted.
    /// </summary>
    [Fact]
    public async Task RunAsync_EveryPersistedEvidenceRow_LinksToAPersistedOpportunity()
    {
        var opportunityRepo = new FakeOpportunityRepository();
        var evidenceRepo = new FakeEvidenceRepository();
        var run = await RunWith(EvidenceWithOutlier(distinctChannels: 3, outlierMultiplier: 1), opportunityRepo, evidenceRepo);

        var opportunityIds = opportunityRepo.Appended.Select(o => o.Id).ToHashSet();

        Assert.NotEmpty(evidenceRepo.Appended);
        Assert.All(evidenceRepo.Appended, e => Assert.Contains(e.OpportunityId, opportunityIds));
        Assert.All(evidenceRepo.Appended, e => Assert.Equal(run.Id.Value, e.RunId));
        Assert.All(evidenceRepo.Appended, e => Assert.Equal(Org, e.OrganizationId));
    }

    /// <summary>
    /// ND-11: opportunities are immutable observations. A later run adds rows; it never edits an
    /// earlier run's. This is asserted at the orchestrator, where a mutation would actually happen —
    /// the repository port having no update method proves only that the easy path is closed.
    /// </summary>
    [Fact]
    public async Task RunAsync_ASecondRun_AppendsWithoutTouchingTheFirstRunsRows()
    {
        var first = CreateRun();
        var second = CreateRun();
        var runRepo = new FakeRunRepository(first);
        await runRepo.AddAsync(second);

        var opportunityRepo = new FakeOpportunityRepository();
        var evidenceRepo = new FakeEvidenceRepository();
        var evidence = EvidenceWithOutlier(distinctChannels: 3, outlierMultiplier: 1);
        var source = FakeSource.Configured("youtube-data-api", evidence);
        var processor = BuildProcessor(runRepo, [source], FakeGateway.SuccessClustering(evidence), opportunityRepo, evidenceRepo);

        await processor.RunAsync(Org, first.Id, CancellationToken.None);
        var afterFirstRun = opportunityRepo.Appended.ToList();
        Assert.NotEmpty(afterFirstRun);

        await processor.RunAsync(Org, second.Id, CancellationToken.None);

        var firstRunRowsNow = opportunityRepo.Appended.Where(o => o.RunId == first.Id.Value).ToList();
        Assert.Equal(afterFirstRun.Count, firstRunRowsNow.Count);
        Assert.Equal(afterFirstRun, firstRunRowsNow);
        Assert.Contains(opportunityRepo.Appended, o => o.RunId == second.Id.Value);
    }

    /// <summary>
    /// A degraded run still has to be useful. `NotEmpty` would pass on rows carrying no ranking at
    /// all, which is precisely the failure an operator could not see.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenTheAiFails_TheDegradedRunStillProducesScoredOpportunities()
    {
        var run = CreateRun();
        var runRepo = new FakeRunRepository(run);
        var source = FakeSource.Configured("youtube-data-api", EvidenceWithOutlier(distinctChannels: 3, outlierMultiplier: 40));
        var opportunityRepo = new FakeOpportunityRepository();
        var processor = BuildProcessor(runRepo, [source], FakeGateway.Failing(), opportunityRepo, new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        Assert.NotEmpty(opportunityRepo.Appended);
        Assert.All(opportunityRepo.Appended, o => Assert.True(o.OpportunityScore > 0, "a degraded run still ranks"));
        Assert.All(opportunityRepo.Appended, o => Assert.True(o.ConfidenceScore > 0, "a degraded run still reports confidence"));
        Assert.All(opportunityRepo.Appended, o => Assert.NotNull(o.MedianViews));
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static async Task<double> ScoreOf(EvidenceSet evidence)
    {
        var opportunityRepo = new FakeOpportunityRepository();
        await RunWith(evidence, opportunityRepo);
        return opportunityRepo.Appended.Max(o => o.OpportunityScore);
    }

    private static async Task<DiscoveryRun> RunWith(
        EvidenceSet evidence,
        FakeOpportunityRepository opportunityRepo,
        FakeEvidenceRepository? evidenceRepo = null)
    {
        var run = CreateRun();
        var runRepo = new FakeRunRepository(run);
        var source = FakeSource.Configured("youtube-data-api", evidence);
        var processor = BuildProcessor(
            runRepo, [source], FakeGateway.SuccessClustering(evidence), opportunityRepo, evidenceRepo ?? new FakeEvidenceRepository());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);
        return run;
    }

    /// <summary>
    /// Six channels: three publishing flat evidence and three carrying a 40x outlier, kept in
    /// separate channel ranges so the two groups can be clustered independently within one run.
    /// </summary>
    private static EvidenceSet TwoClusterEvidence(
        out IReadOnlyList<string> flatVideoRefs,
        out IReadOnlyList<string> outlierVideoRefs)
    {
        var flat = EvidenceWithOutlier(distinctChannels: 3, outlierMultiplier: 1, channelOffset: 0);
        var hot = EvidenceWithOutlier(distinctChannels: 3, outlierMultiplier: 40, channelOffset: 10);

        flatVideoRefs = flat.Videos.Select(v => v.ExternalRef).ToList();
        outlierVideoRefs = hot.Videos.Select(v => v.ExternalRef).ToList();

        return new EvidenceSet(
            [.. flat.Channels, .. hot.Channels],
            [.. flat.Videos, .. hot.Videos]);
    }

    /// <summary>
    /// Several videos per channel so a channel median exists at all, optionally with one video far
    /// above that median. <paramref name="outlierMultiplier"/> of 1 produces deliberately flat
    /// evidence — the control case.
    /// </summary>
    private static EvidenceSet EvidenceWithOutlier(int distinctChannels, int outlierMultiplier, int channelOffset = 0)
    {
        var channels = new List<ChannelEvidence>();
        var videos = new List<VideoEvidence>();

        for (var index = 0; index < distinctChannels; index++)
        {
            var i = index + channelOffset;
            var channelRef = $"channel-{i}";
            channels.Add(new ChannelEvidence(channelRef, $"handle-{i}", "AR", 5000 + i * 100, 500000, 80, "es",
                Now.AddYears(-2), null, null, "youtube-data-api"));

            var baseViews = 10000 + i * 500;

            for (var v = 0; v < 4; v++)
            {
                videos.Add(new VideoEvidence($"video-{i}-{v}", channelRef, "Budget Travel Guide", "budget travel tips",
                    Now.AddDays(-1), baseViews, 200, 30, 480, "es", "youtube-data-api"));
            }

            if (outlierMultiplier > 1)
            {
                videos.Add(new VideoEvidence($"video-{i}-outlier", channelRef, "Budget Travel Guide", "budget travel tips",
                    Now.AddDays(-1), baseViews * (long)outlierMultiplier, 9000, 1200, 480, "es", "youtube-data-api"));
            }
        }

        return new EvidenceSet(channels, videos);
    }

    private static NicheDiscoveryRunProcessor BuildProcessor(
        FakeRunRepository runRepo,
        IEnumerable<INicheDiscoverySource> sources,
        IAiCompletionGateway ai,
        FakeOpportunityRepository opportunityRepo,
        FakeEvidenceRepository evidenceRepo) =>
        new(runRepo, sources, ai, opportunityRepo, evidenceRepo, new FakeClock(Now), new FakeWorkflowResolver());


    /// <summary>An enabled AI route, so the semantic layer is reachable in these tests.</summary>
    private sealed class FakeWorkflowResolver : SocialStudio.Application.Abstractions.Integrations.IWorkflowConfigurationResolver
    {
        public Task<Result<IReadOnlyList<SocialStudio.Application.Abstractions.Integrations.WorkflowStepRouteCandidate>>> ResolveStepAsync(
            SocialStudio.Application.Abstractions.Integrations.WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<SocialStudio.Application.Abstractions.Integrations.WorkflowStepRouteCandidate>>(
                [new SocialStudio.Application.Abstractions.Integrations.WorkflowStepRouteCandidate(
                    ProviderId: Guid.NewGuid(),
                    ProviderKey: "claude",
                    OrganizationIntegrationId: Guid.NewGuid(),
                    ConnectionMode: SocialStudio.Domain.Integrations.IntegrationConnectionMode.BringYourOwnKey,
                    Priority: 1,
                    ModelId: null,
                    ActorId: null,
                    IsEnabled: true)]));
    }

    /// <summary>More clusters than any depth ceiling allows, so the cut is actually exercised.</summary>
    private const int ClusterCount = 40;

    private static EvidenceSet WideEvidence(int clusters)
    {
        var channels = new List<ChannelEvidence>();
        var videos = new List<VideoEvidence>();

        for (var i = 0; i < clusters; i++)
        {
            channels.Add(new ChannelEvidence(
                $"channel-{i}", $"handle-{i}", "AR", 5000 + i * 100, 500000 + i, 80, "es",
                Now.AddYears(-2), null, null, "youtube-data-api"));
            videos.Add(new VideoEvidence(
                $"video-{i}", $"channel-{i}", $"Topic {i}", $"about topic {i}",
                Now.AddDays(-1), 10000 + i * 500, 200, 30, 480, "es", "youtube-data-api"));
        }

        return new EvidenceSet(channels, videos);
    }

    private static (string Label, IReadOnlyList<string> VideoRefs)[] WideClusters(int clusters) =>
        Enumerable.Range(0, clusters)
            .Select(i => ($"topic-{i}", (IReadOnlyList<string>)new[] { $"video-{i}" }))
            .ToArray();

    private static DiscoveryRun CreateRun(
        IReadOnlyList<string>? seedTerms = null, DiscoveryDepth depth = DiscoveryDepth.Standard)
    {
        var seedTermsJson = new DiscoveryRunConfigurationSnapshot(seedTerms ?? ["budget travel"]).ToJson();

        var result = DiscoveryRun.Create(
            Org, User, "es-AR", "pt-BR", "youtube", "video",
            DiscoveryWindow.Default(), depth, seedTermsJson, nicheId: null, Now);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>Three distinct channels publishing three videos sharing a repeated token, so the
    /// lexical fallback path (K=3 distinct channels) can cluster them too.</summary>
    private static EvidenceSet Evidence(int distinctChannels)
    {
        var channels = new List<ChannelEvidence>();
        var videos = new List<VideoEvidence>();

        for (var i = 0; i < distinctChannels; i++)
        {
            var channelRef = $"channel-{i}";
            channels.Add(new ChannelEvidence(channelRef, $"handle-{i}", "AR", 5000 + i * 100, 500000, 80, "es",
                Now.AddYears(-2), null, null, "youtube-data-api"));
            videos.Add(new VideoEvidence($"video-{i}", channelRef, "Budget Travel Guide", "budget travel tips",
                Now.AddDays(-1), 10000 + i * 500, 200, 30, 480, "es", "youtube-data-api"));
        }

        return new EvidenceSet(channels, videos);
    }

    private sealed class FakeRunRepository : IDiscoveryRunRepository
    {
        public FakeRunRepository(DiscoveryRun seed)
        {
            Runs[seed.Id] = seed;
        }

        public Dictionary<DiscoveryRunId, DiscoveryRun> Runs { get; } = new();

        public List<DiscoveryRunStatus> ObservedStatuses { get; } = [];

        public Task AddAsync(DiscoveryRun run, CancellationToken ct = default)
        {
            Runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DiscoveryRun run, CancellationToken ct = default)
        {
            Runs[run.Id] = run;
            ObservedStatuses.Add(run.Status);
            return Task.CompletedTask;
        }

        public Task<DiscoveryRun?> GetByIdAsync(Guid organizationId, DiscoveryRunId id, CancellationToken ct = default) =>
            Task.FromResult(Runs.TryGetValue(id, out var run) && run.OrganizationId == organizationId ? run : null);

        public Task<IReadOnlyList<DiscoveryRun>> ListByOrganizationAsync(Guid organizationId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DiscoveryRun>>(
                Runs.Values.Where(r => r.OrganizationId == organizationId).Take(limit).ToList());
    }

    private sealed class FakeSource : INicheDiscoverySource
    {
        private readonly NicheDiscoverySourceResult _result;

        private FakeSource(string sourceKey, NicheDiscoverySourceResult result)
        {
            SourceKey = sourceKey;
            _result = result;
        }

        public static FakeSource Configured(string sourceKey, EvidenceSet evidence) =>
            new(sourceKey, NicheDiscoverySourceResult.Ran(evidence.Channels, evidence.Videos, new BudgetSpend(50, 0)));

        public static FakeSource Unconfigured(string sourceKey) =>
            new(sourceKey, NicheDiscoverySourceResult.Unconfigured());

        public static FakeSource Failing(string sourceKey, string reasonKey) =>
            new(sourceKey, NicheDiscoverySourceResult.Failed(reasonKey));

        public string SourceKey { get; }

        public int Order => 1;

        public bool Supports(DiscoveryStage stage) => stage is DiscoveryStage.ChannelSearch or DiscoveryStage.VideoSearch;

        public Task<NicheDiscoverySourceResult> DiscoverAsync(
            NicheDiscoveryQuery query, DiscoveryBudget budget, CancellationToken ct = default)
        {
            // Only VideoSearch carries videos in this fixture; ChannelSearch reports the same channels.
            if (query.Stage == DiscoveryStage.ChannelSearch)
            {
                return Task.FromResult(_result.Outcome == NicheSourceOutcome.Ran
                    ? NicheDiscoverySourceResult.Ran(_result.Channels, [], _result.Spend)
                    : _result);
            }

            return Task.FromResult(_result.Outcome == NicheSourceOutcome.Ran
                ? NicheDiscoverySourceResult.Ran([], _result.Videos, _result.Spend)
                : _result);
        }
    }

    private sealed class FakeGateway : IAiCompletionGateway
    {
        private readonly bool _fails;

        private readonly (string Label, IReadOnlyList<string> VideoRefs)[] _clusters;

        private FakeGateway(bool fails, IReadOnlyList<string> clusteredVideoRefs)
        {
            _fails = fails;
            _clusteredVideoRefs = clusteredVideoRefs;
            _clusters = [];
        }

        private FakeGateway((string Label, IReadOnlyList<string> VideoRefs)[] clusters)
        {
            _fails = false;
            _clusteredVideoRefs = [];
            _clusters = clusters;
        }

        private readonly IReadOnlyList<string> _clusteredVideoRefs;

        public static FakeGateway Success() => new(false, ["video-0", "video-1", "video-2"]);

        /// <summary>
        /// Clusters exactly the videos the fixture actually produced. A gateway that returns fixed
        /// refs silently clusters nothing when the evidence changes, and every downstream metric then
        /// comes back null for a reason that has nothing to do with the code under test.
        /// </summary>
        public static FakeGateway SuccessClustering(EvidenceSet evidence) =>
            new(false, evidence.Videos.Select(v => v.ExternalRef).ToList());

        /// <summary>Returns several named clusters, so one run can hold more than one opportunity.</summary>
        public static FakeGateway SuccessWithClusters(params (string Label, IReadOnlyList<string> VideoRefs)[] clusters) =>
            new(clusters);

        public static FakeGateway Failing() => new(true, []);

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request, CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            if (_fails)
            {
                return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                    Error.Failure("ai.provider-down", "provider unavailable")));
            }

            var payload = _clusters.Length > 0
                ? new SemanticAnnotation(
                    [.. _clusters.Select(c => new ClusterProposal(c.Label, "Listicle", c.VideoRefs))],
                    [.. _clusters.Select(c => c.Label)],
                    ["ScreenRecording"])
                : new SemanticAnnotation(
                    [new ClusterProposal("budget travel", "Listicle", _clusteredVideoRefs)],
                    ["budget-travel"],
                    ["ScreenRecording"]);

            return Task.FromResult(Result.Success(
                new AiJsonCompletionResponse<TResponse>((TResponse)(object)payload, "fake", "fake-model", [])));
        }
    }

    private sealed class FakeOpportunityRepository : INicheOpportunityRepository
    {
        public List<NicheOpportunityRecord> Appended { get; } = [];

        public Task AppendAsync(IReadOnlyList<NicheOpportunityRecord> opportunities, CancellationToken ct = default)
        {
            Appended.AddRange(opportunities);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NicheOpportunityRecord>> ListByRunAsync(Guid organizationId, Guid runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NicheOpportunityRecord>>(Appended.Where(o => o.RunId == runId).ToList());

        public Task<NicheOpportunityRecord?> GetByIdAsync(Guid organizationId, Guid opportunityId, CancellationToken ct = default) =>
            Task.FromResult(Appended.FirstOrDefault(o => o.Id == opportunityId));
    }

    private sealed class FakeEvidenceRepository : INicheDiscoveryEvidenceRepository
    {
        public List<NicheDiscoveryEvidenceRecord> Appended { get; } = [];

        public Task AppendAsync(IReadOnlyList<NicheDiscoveryEvidenceRecord> evidence, CancellationToken ct = default)
        {
            Appended.AddRange(evidence);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NicheDiscoveryEvidenceRecord>> ListByOpportunityAsync(Guid organizationId, Guid opportunityId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NicheDiscoveryEvidenceRecord>>(Appended.Where(e => e.OpportunityId == opportunityId).ToList());
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }
}
