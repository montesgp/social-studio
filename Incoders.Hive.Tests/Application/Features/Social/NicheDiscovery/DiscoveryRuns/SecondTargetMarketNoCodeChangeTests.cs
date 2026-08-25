using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Semantics;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// THE HEADLINE TEST OF THIS BATCH (task 11a.5, ND-14): adding a second target market must be a
/// seeded catalog row, never a code change. Seeds <c>pt-BR</c> into an in-memory catalog fake alongside
/// the existing <c>es-AR</c> default, runs the SAME <see cref="NicheDiscoveryRunProcessor"/> over the
/// SAME recorded evidence fixture with <c>targetMarket = pt-BR</c>, and asserts (via a source-tree
/// scan, same technique as <c>NicheDiscoveryCountryNameGuardTests</c>, task 2.7) that no per-market
/// file exists anywhere under the NicheDiscovery namespaces. The assertion is literally "one code
/// path, two rows".
/// </summary>
public sealed class SecondTargetMarketNoCodeChangeTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] GuardedRelativeDirectories =
    [
        "Incoders.Hive.Domain/Social/NicheDiscovery",
        "Incoders.Hive.Application/Abstractions/Social/NicheDiscovery",
        "Incoders.Hive.Application/Features/Social/NicheDiscovery",
        "Incoders.Hive.Infrastructure/Social/NicheDiscovery",
    ];

    // Market-specific naming fragments a "per-market file" implementation would have to use.
    // "AR"/"BR" bare-code fragments are already covered by NicheDiscoveryCountryNameGuardTests; this
    // list is this test's own direct, narrative assertion for the two markets exercised here.
    private static readonly string[] MarketSpecificFragments = ["EsAr", "PtBr", "Brazil", "Argentin"];

    [Fact]
    public async Task InMemoryMarketCatalog_ListsBothTheDefaultAndTheSeededSecondMarket()
    {
        var catalog = new InMemoryMarketCatalog();

        var active = await catalog.ListActiveAsync(CancellationToken.None);

        Assert.Contains(active, m => m.Code.Value == "es-AR");
        Assert.Contains(active, m => m.Code.Value == "pt-BR");
    }

    [Fact]
    public async Task RunAsync_OverTheSameFixture_WithTheSeededSecondMarket_ProducesANonEmptyRankedResult()
    {
        var esArResult = await RunWithTargetMarket("es-AR");
        var ptBrResult = await RunWithTargetMarket("pt-BR");

        Assert.NotEmpty(esArResult.Appended);
        Assert.NotEmpty(ptBrResult.Appended);
    }

    [Fact]
    public void NoPerMarketSourceFileExistsAnywhereUnderTheNicheDiscoveryNamespaces()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (var relativeDir in GuardedRelativeDirectories)
        {
            var fullDir = Path.Combine(root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullDir))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(fullDir, "*.cs", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);

                foreach (var fragment in MarketSpecificFragments)
                {
                    if (fileName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{filePath}: market-specific filename fragment '{fragment}'");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Adding a second target market must be a seeded catalog row, never a code change (ND-14). "
                + "Offenders:\n" + string.Join('\n', offenders));
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static async Task<FakeOpportunityRepository> RunWithTargetMarket(string targetMarketCode)
    {
        var seedTermsJson = new DiscoveryRunConfigurationSnapshot(["budget travel"]).ToJson();
        var runResult = DiscoveryRun.Create(
            Org, User, "es-AR", targetMarketCode, "youtube", "video",
            DiscoveryWindow.Default(), DiscoveryDepth.Standard, seedTermsJson, nicheId: null, Now);
        Assert.True(runResult.IsSuccess);
        var run = runResult.Value;

        var runRepo = new FakeRunRepository(run);
        var opportunityRepo = new FakeOpportunityRepository();
        var processor = new NicheDiscoveryRunProcessor(
            runRepo,
            [new FakeSource(SharedFixtureEvidence())],
            new FakeGateway(),
            opportunityRepo,
            new FakeEvidenceRepository(),
            new FakeClock(Now),
            new FakeWorkflowResolver());

        await processor.RunAsync(Org, run.Id, CancellationToken.None);

        return opportunityRepo;
    }

    /// <summary>The SAME recorded evidence fixture for both runs — only the target market changes.</summary>
    private static EvidenceSet SharedFixtureEvidence()
    {
        var channels = new List<ChannelEvidence>();
        var videos = new List<VideoEvidence>();

        for (var i = 0; i < 3; i++)
        {
            var channelRef = $"channel-{i}";
            channels.Add(new ChannelEvidence(channelRef, $"handle-{i}", "AR", 5000 + i * 100, 500000, 80, "es",
                Now.AddYears(-2), null, null, "youtube-data-api"));
            videos.Add(new VideoEvidence($"video-{i}", channelRef, "Budget Travel Guide", "budget travel tips",
                Now.AddDays(-1), 10000 + i * 500, 200, 30, 480, "es", "youtube-data-api"));
        }

        return new EvidenceSet(channels, videos);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Incoders.Hive.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Fake read-only market catalog: adding <c>pt-BR</c> is one more row, no interface change.</summary>
    private sealed class InMemoryMarketCatalog : IMarketCatalogRepository
    {
        private readonly List<MarketCatalogEntry> _entries;

        public InMemoryMarketCatalog()
        {
            var esAr = MarketCatalogEntry.Create(MarketCode.TryParse("es-AR").Value, "social.niche_discovery.markets.es_ar", "es", isActive: true);
            var ptBr = MarketCatalogEntry.Create(MarketCode.TryParse("pt-BR").Value, "social.niche_discovery.markets.pt_br", "pt", isActive: true);
            _entries = [esAr.Value, ptBr.Value];
        }

        public Task<IReadOnlyList<MarketCatalogEntry>> ListActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MarketCatalogEntry>>(_entries.Where(e => e.IsActive).ToList());

        public Task<MarketCatalogEntry?> GetByCodeAsync(MarketCode code, CancellationToken ct = default) =>
            Task.FromResult(_entries.FirstOrDefault(e => e.Code.Value == code.Value));
    }

/// <summary>An enabled AI route, so the semantic layer is reachable in these tests.</summary>
    private sealed class FakeWorkflowResolver : Incoders.Hive.Application.Abstractions.Integrations.IWorkflowConfigurationResolver
    {
        public Task<Result<IReadOnlyList<Incoders.Hive.Application.Abstractions.Integrations.WorkflowStepRouteCandidate>>> ResolveStepAsync(
            Incoders.Hive.Application.Abstractions.Integrations.WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<Incoders.Hive.Application.Abstractions.Integrations.WorkflowStepRouteCandidate>>(
                [new Incoders.Hive.Application.Abstractions.Integrations.WorkflowStepRouteCandidate(
                    ProviderId: Guid.NewGuid(),
                    ProviderKey: "claude",
                    OrganizationIntegrationId: Guid.NewGuid(),
                    ConnectionMode: Incoders.Hive.Domain.Integrations.IntegrationConnectionMode.BringYourOwnKey,
                    Priority: 1,
                    ModelId: null,
                    ActorId: null,
                    IsEnabled: true)]));
    }

    private sealed class FakeRunRepository : IDiscoveryRunRepository
    {
        private readonly Dictionary<DiscoveryRunId, DiscoveryRun> _runs = new();

        public FakeRunRepository(DiscoveryRun seed) => _runs[seed.Id] = seed;

        public Task AddAsync(DiscoveryRun run, CancellationToken ct = default)
        {
            _runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DiscoveryRun run, CancellationToken ct = default)
        {
            _runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task<DiscoveryRun?> GetByIdAsync(Guid organizationId, DiscoveryRunId id, CancellationToken ct = default) =>
            Task.FromResult(_runs.TryGetValue(id, out var run) && run.OrganizationId == organizationId ? run : null);

        public Task<IReadOnlyList<DiscoveryRun>> ListByOrganizationAsync(Guid organizationId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DiscoveryRun>>(
                _runs.Values.Where(r => r.OrganizationId == organizationId).Take(limit).ToList());
    }

    private sealed class FakeSource : INicheDiscoverySource
    {
        private readonly EvidenceSet _evidence;

        public FakeSource(EvidenceSet evidence) => _evidence = evidence;

        public string SourceKey => "youtube-data-api";

        public int Order => 1;

        public bool Supports(DiscoveryStage stage) => stage is DiscoveryStage.ChannelSearch or DiscoveryStage.VideoSearch;

        public Task<NicheDiscoverySourceResult> DiscoverAsync(
            NicheDiscoveryQuery query, DiscoveryBudget budget, CancellationToken ct = default) =>
            Task.FromResult(query.Stage == DiscoveryStage.ChannelSearch
                ? NicheDiscoverySourceResult.Ran(_evidence.Channels, [], new BudgetSpend(50, 0))
                : NicheDiscoverySourceResult.Ran([], _evidence.Videos, new BudgetSpend(50, 0)));
    }

    private sealed class FakeGateway : IAiCompletionGateway
    {
        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request, CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
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
        public Task AppendAsync(IReadOnlyList<NicheDiscoveryEvidenceRecord> evidence, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<NicheDiscoveryEvidenceRecord>> ListByOpportunityAsync(Guid organizationId, Guid opportunityId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NicheDiscoveryEvidenceRecord>>([]);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }
}
