using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Semantics;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

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

    // ── harness ──────────────────────────────────────────────────────────────

    private static NicheDiscoveryRunProcessor BuildProcessor(
        FakeRunRepository runRepo,
        IEnumerable<INicheDiscoverySource> sources,
        IAiCompletionGateway ai,
        FakeOpportunityRepository opportunityRepo,
        FakeEvidenceRepository evidenceRepo) =>
        new(runRepo, sources, ai, opportunityRepo, evidenceRepo, new FakeClock(Now));

    private static DiscoveryRun CreateRun()
    {
        var seedTermsJson = new DiscoveryRunConfigurationSnapshot(["budget travel"]).ToJson();

        var result = DiscoveryRun.Create(
            Org, User, "es-AR", "pt-BR", "youtube", "video",
            DiscoveryWindow.Default(), DiscoveryDepth.Standard, seedTermsJson, nicheId: null, Now);

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
                Now.AddYears(-2), "youtube-data-api"));
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

        private FakeGateway(bool fails)
        {
            _fails = fails;
        }

        public static FakeGateway Success() => new(false);

        public static FakeGateway Failing() => new(true);

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request, CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            if (_fails)
            {
                return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(
                    Error.Failure("ai.provider-down", "provider unavailable")));
            }

            var payload = new SemanticAnnotation(
                [new ClusterProposal("budget travel", "Listicle", ["video-0", "video-1", "video-2"])],
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
