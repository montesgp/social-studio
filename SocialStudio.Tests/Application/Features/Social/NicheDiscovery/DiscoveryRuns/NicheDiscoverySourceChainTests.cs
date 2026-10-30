using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Tests.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// RED tests for <see cref="NicheDiscoverySourceChain"/> (task 11a.1, design §5.1). Mirrors
/// <c>ContentIdeaChainTests</c>: fake sources, per-stage <c>Supports</c> selection, budget threading
/// through <see cref="IDiscoveryBudgetLedger"/>, and the three-disjoint-outcome discipline (ND-03).
/// </summary>
public sealed class NicheDiscoverySourceChainTests
{
    private static readonly Guid Org = Guid.NewGuid();

    [Fact]
    public async Task RunStageAsync_OnlyCallsSourcesThatSupportTheStage()
    {
        var channelSource = new FakeSource("channel-only", 1, DiscoveryStage.ChannelSearch,
            NicheDiscoverySourceResult.Ran([Channel("c1")], [], new BudgetSpend(10, 0)));
        var videoSource = new FakeSource("video-only", 2, DiscoveryStage.VideoSearch,
            NicheDiscoverySourceResult.Ran([], [Video("v1", "c1")], new BudgetSpend(10, 0)));
        var chain = new NicheDiscoverySourceChain([channelSource, videoSource]);
        var ledger = NewLedger();

        var result = await chain.RunStageAsync(Query(DiscoveryStage.ChannelSearch), ledger, CancellationToken.None);

        Assert.True(channelSource.WasCalled);
        Assert.False(videoSource.WasCalled);
        Assert.Single(result.Channels);
        Assert.Empty(result.Videos);
        Assert.Equal(["channel-only"], result.RanSourceKeys);
    }

    [Fact]
    public async Task RunStageAsync_UnconfiguredAndFailedAndRanAreThreeDisjointReports()
    {
        var unconfigured = new FakeSource("unconfigured", 1, DiscoveryStage.VideoSearch,
            NicheDiscoverySourceResult.Unconfigured());
        var failing = new FakeSource("failing", 2, DiscoveryStage.VideoSearch,
            NicheDiscoverySourceResult.Failed("social.niche_discovery.provider_quota_exhausted"));
        var ran = new FakeSource("ran", 3, DiscoveryStage.VideoSearch,
            NicheDiscoverySourceResult.Ran([], [Video("v1", "c1")], new BudgetSpend(5, 0)));
        var chain = new NicheDiscoverySourceChain([unconfigured, failing, ran]);
        var ledger = NewLedger();

        var result = await chain.RunStageAsync(Query(DiscoveryStage.VideoSearch), ledger, CancellationToken.None);

        Assert.Equal(["unconfigured"], result.UnconfiguredSourceKeys);
        Assert.Equal(["failing"], result.FailedSourceKeys);
        Assert.Equal(["ran"], result.RanSourceKeys);
        Assert.Single(result.Videos);
    }

    [Fact]
    public async Task RunStageAsync_OneFailingOriginDoesNotEmptyTheRunWhenAnotherStillContributes()
    {
        // ND-03: a failing/unconfigured origin never blanks out what a sibling origin still gathered.
        var failing = new FakeSource("failing", 1, DiscoveryStage.VideoSearch,
            NicheDiscoverySourceResult.Failed("social.niche_discovery.provider_quota_exhausted"));
        var contributing = new FakeSource("contributing", 2, DiscoveryStage.VideoSearch,
            NicheDiscoverySourceResult.Ran([], [Video("v1", "c1"), Video("v2", "c1")], new BudgetSpend(5, 0)));
        var chain = new NicheDiscoverySourceChain([failing, contributing]);
        var ledger = NewLedger();

        var result = await chain.RunStageAsync(Query(DiscoveryStage.VideoSearch), ledger, CancellationToken.None);

        Assert.Equal(2, result.Videos.Count);
        Assert.Contains("failing", result.FailedSourceKeys);
    }

    [Fact]
    public async Task RunStageAsync_ThreadsSpendThroughTheBudgetLedger()
    {
        var source = new FakeSource("youtube-data-api", 1, DiscoveryStage.VideoSearch,
            NicheDiscoverySourceResult.Ran([], [Video("v1", "c1")], new BudgetSpend(120, 0)));
        var chain = new NicheDiscoverySourceChain([source]);
        var ledger = NewLedger();

        await chain.RunStageAsync(Query(DiscoveryStage.VideoSearch), ledger, CancellationToken.None);

        Assert.Equal(DiscoveryBudget.ForDepth(DiscoveryDepth.Standard).ProviderUnits - 120, ledger.RemainingProviderUnits);
    }

    [Fact]
    public async Task RunStageAsync_RunsSourcesInOrderRegardlessOfRegistration()
    {
        var second = new FakeSource("second", 5, DiscoveryStage.VideoSearch,
            NicheDiscoverySourceResult.Ran([], [Video("v-second", "c1")], BudgetSpend.None));
        var first = new FakeSource("first", 1, DiscoveryStage.VideoSearch,
            NicheDiscoverySourceResult.Ran([], [Video("v-first", "c1")], BudgetSpend.None));
        var chain = new NicheDiscoverySourceChain([second, first]);
        var ledger = NewLedger();

        var result = await chain.RunStageAsync(Query(DiscoveryStage.VideoSearch), ledger, CancellationToken.None);

        Assert.Equal(["first", "second"], result.RanSourceKeys);
    }

    private static InMemoryDiscoveryBudgetLedger NewLedger() =>
        new(DiscoveryBudget.ForDepth(DiscoveryDepth.Standard));

    private static NicheDiscoveryQuery Query(DiscoveryStage stage) => new(
        Org, stage, "youtube", "video", "es", DiscoveryWindow.Default(), ["term"], [], []);

    private static ChannelEvidence Channel(string externalRef) =>
        new(externalRef, "handle", "AR", 1000, 100000, 50, "es", DateTimeOffset.UtcNow.AddYears(-2), null, null, "fake");

    private static VideoEvidence Video(string externalRef, string channelExternalRef) =>
        new(externalRef, channelExternalRef, "title", "description", DateTimeOffset.UtcNow.AddDays(-1),
            1000, 10, 2, 300, "es", "fake");

    private sealed class FakeSource : INicheDiscoverySource
    {
        private readonly DiscoveryStage _supportedStage;
        private readonly NicheDiscoverySourceResult _result;

        public FakeSource(string sourceKey, int order, DiscoveryStage supportedStage, NicheDiscoverySourceResult result)
        {
            SourceKey = sourceKey;
            Order = order;
            _supportedStage = supportedStage;
            _result = result;
        }

        public string SourceKey { get; }

        public int Order { get; }

        public bool WasCalled { get; private set; }

        public bool Supports(DiscoveryStage stage) => stage == _supportedStage;

        public Task<NicheDiscoverySourceResult> DiscoverAsync(
            NicheDiscoveryQuery query, DiscoveryBudget budget, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(_result);
        }
    }

    private sealed class InMemoryDiscoveryBudgetLedger : IDiscoveryBudgetLedger
    {
        private int _remainingProviderUnits;
        private int _remainingAiCompletions;

        public InMemoryDiscoveryBudgetLedger(DiscoveryBudget budget)
        {
            _remainingProviderUnits = budget.ProviderUnits;
            _remainingAiCompletions = budget.AiCompletions;
        }

        public int RemainingProviderUnits => _remainingProviderUnits;

        public int RemainingAiCompletions => _remainingAiCompletions;

        public bool CanAfford(int providerUnits, int aiCompletions = 0) =>
            providerUnits <= _remainingProviderUnits && aiCompletions <= _remainingAiCompletions;

        public void Spend(int providerUnits, int aiCompletions = 0)
        {
            _remainingProviderUnits -= providerUnits;
            _remainingAiCompletions -= aiCompletions;
        }
    }
}
