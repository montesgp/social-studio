using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// WU-2: mirrors <c>ContentIdeaChainTests</c> in shape. CD-01 (ordered, remainder-only, one failing
/// source never empties the run), CD-02 (cross-source dedup), CD-03 (criterion routing), CD-13/D12
/// (Unconfigured is reported separately from Failed and from a legitimate empty Ran).
/// </summary>
public sealed class ContentDiscoveryChainTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    [Fact]
    public async Task OneSourceFails_OthersStillContribute()
    {
        var failing = new FakeSource("failing", order: 1, supports: "*", behavior: SourceBehavior.Throw);
        var surviving = new FakeSource("surviving", order: 2, supports: "*", candidates: [Candidate("surviving", "https://tiktok.com/@a/video/1")]);

        var chain = new ContentDiscoveryChain([failing, surviving]);

        var plan = await chain.DiscoverAsync(Request("references", 20), CancellationToken.None);

        Assert.Single(plan.Candidates);
        Assert.Contains("failing", plan.FailedSourceKeys);
        Assert.Equal(19, plan.Shortfall);
    }

    [Fact]
    public async Task ChainAsksEachSourceOnlyForTheRemainder()
    {
        var first = new FakeSource("first", order: 1, supports: "*", candidates: Many(12, "first"));
        var second = new FakeSource("second", order: 2, supports: "*");

        var chain = new ContentDiscoveryChain([first, second]);

        await chain.DiscoverAsync(Request("references", 20), CancellationToken.None);

        Assert.Equal(8, second.LastRequestedMaxResults);
    }

    [Fact]
    public async Task Duplicates_AcrossSources_AreDeduplicated()
    {
        var shared = "https://tiktok.com/@a/video/1";
        var first = new FakeSource("first", order: 1, supports: "*", candidates: [Candidate("first", shared)]);
        var second = new FakeSource("second", order: 2, supports: "*", candidates: [Candidate("second", shared)]);

        var chain = new ContentDiscoveryChain([first, second]);

        var plan = await chain.DiscoverAsync(Request("references", 20), CancellationToken.None);

        Assert.Single(plan.Candidates);
    }

    [Fact]
    public async Task NicheCriterion_ConsultsOnlyTheNicheSource()
    {
        var references = new FakeSource("references", order: 1, supports: "references", candidates: [Candidate("references", "https://ig.com/p/1")]);
        var niche = new FakeSource("niche", order: 3, supports: "niche", candidates: [Candidate("niche", "https://tiktok.com/@a/video/1")]);

        var chain = new ContentDiscoveryChain([references, niche]);

        var plan = await chain.DiscoverAsync(Request("niche", 20), CancellationToken.None);

        Assert.Single(plan.Candidates);
        Assert.Equal("niche", plan.Candidates[0].SourceKey);
    }

    [Fact]
    public async Task OwnCriterion_ConsultsOnlyTheOwnSource()
    {
        var references = new FakeSource("references", order: 1, supports: "references", candidates: [Candidate("references", "https://ig.com/p/1")]);
        var own = new FakeSource("own", order: 2, supports: "own", candidates: [Candidate("own", "https://ig.com/p/2")]);

        var chain = new ContentDiscoveryChain([references, own]);

        var plan = await chain.DiscoverAsync(Request("own", 20), CancellationToken.None);

        Assert.Single(plan.Candidates);
        Assert.Equal("own", plan.Candidates[0].SourceKey);
    }

    [Theory]
    [InlineData("viral")]
    [InlineData("competitors")]
    public async Task UnsupportedCriteria_FallBackToTheDefaultChain_NeverAnError(string criterion)
    {
        var references = new FakeSource("references", order: 1, supports: "references", candidates: [Candidate("references", "https://ig.com/p/1")]);
        var own = new FakeSource("own", order: 2, supports: "own", candidates: [Candidate("own", "https://ig.com/p/2")]);

        var chain = new ContentDiscoveryChain([references, own]);

        var plan = await chain.DiscoverAsync(Request(criterion, 20), CancellationToken.None);

        Assert.Equal(2, plan.Candidates.Count);
    }

    [Fact]
    public async Task Unconfigured_LandsInUnconfiguredList_RanWithZero_LandsInNeitherList()
    {
        var unconfigured = new FakeSource("niche", order: 3, supports: "*", behavior: SourceBehavior.Unconfigured);
        var ranEmpty = new FakeSource("references", order: 1, supports: "*", candidates: []);

        var chain = new ContentDiscoveryChain([unconfigured, ranEmpty]);

        var plan = await chain.DiscoverAsync(Request("viral", 20), CancellationToken.None);

        Assert.Contains("niche", plan.UnconfiguredSourceKeys);
        Assert.DoesNotContain("references", plan.UnconfiguredSourceKeys);
        Assert.DoesNotContain("references", plan.FailedSourceKeys);
        Assert.DoesNotContain("niche", plan.FailedSourceKeys);
        Assert.Empty(plan.Candidates);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static ContentDiscoveryRequest Request(string criterion, int max) =>
        new(Org, Client, [], max, criterion);

    private static DiscoveredContent Candidate(string sourceKey, string url) =>
        new("t", "s", "tiktok", url, null, 1_000, 10, 1, null, null, DateTimeOffset.UtcNow, "@a", sourceKey);

    private static IReadOnlyList<DiscoveredContent> Many(int count, string sourceKey) =>
        Enumerable.Range(1, count)
            .Select(i => Candidate(sourceKey, $"https://tiktok.com/@a/video/{i}"))
            .ToList();

    private enum SourceBehavior
    {
        ReturnCandidates,
        Throw,
        Unconfigured,
        Failed
    }

    private sealed class FakeSource : IContentDiscoverySource
    {
        private readonly string _supports;
        private readonly IReadOnlyList<DiscoveredContent> _candidates;
        private readonly SourceBehavior _behavior;

        public FakeSource(
            string sourceKey,
            int order,
            string supports,
            IReadOnlyList<DiscoveredContent>? candidates = null,
            SourceBehavior behavior = SourceBehavior.ReturnCandidates)
        {
            SourceKey = sourceKey;
            Order = order;
            _supports = supports;
            _candidates = candidates ?? [];
            _behavior = behavior;
        }

        public string SourceKey { get; }

        public int Order { get; }

        public int LastRequestedMaxResults { get; private set; }

        public bool Supports(string searchCriterion) =>
            _supports == "*" || string.Equals(_supports, searchCriterion, StringComparison.OrdinalIgnoreCase);

        public Task<SourceDiscoveryResult> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct = default)
        {
            LastRequestedMaxResults = request.MaxResults;

            return _behavior switch
            {
                SourceBehavior.Throw => throw new InvalidOperationException("provider down"),
                SourceBehavior.Unconfigured => Task.FromResult(new SourceDiscoveryResult(SourceOutcome.Unconfigured, [])),
                SourceBehavior.Failed => Task.FromResult(new SourceDiscoveryResult(SourceOutcome.Failed, [])),
                _ => Task.FromResult(new SourceDiscoveryResult(SourceOutcome.Ran, _candidates)),
            };
        }
    }
}
