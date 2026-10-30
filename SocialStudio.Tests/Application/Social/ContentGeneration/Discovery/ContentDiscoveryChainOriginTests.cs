using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// The chain has to carry the ORIGIN of what it collected up to the operator. Serving stored
/// candidates silently is how an operator ends up working from stale content without knowing.
/// </summary>
public sealed class ContentDiscoveryChainOriginTests
{
    private static readonly DateTimeOffset Observed = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EveryContributingSourceServedFromTheBank_ThePlanReportsTheBank()
    {
        var chain = new ContentDiscoveryChain(
        [
            new StubSource("niche", new SourceDiscoveryResult(
                SourceOutcome.Ran, [Content("https://tiktok.com/@a/video/1")], DiscoveryOrigin.Bank, Observed)),
        ]);

        var plan = await chain.DiscoverAsync(Request());

        Assert.Single(plan.Candidates);
        Assert.Equal(DiscoveryOrigin.Bank, plan.Origin);
        Assert.Equal(Observed, plan.BankObservedAtUtc);
    }

    [Fact]
    public async Task AnyFreshlyScrapedCandidate_MakesThePlanFresh()
    {
        var chain = new ContentDiscoveryChain(
        [
            new StubSource("niche", new SourceDiscoveryResult(
                SourceOutcome.Ran, [Content("https://tiktok.com/@a/video/1")], DiscoveryOrigin.Bank, Observed)),
            new StubSource("references", new SourceDiscoveryResult(
                SourceOutcome.Ran, [Content("https://tiktok.com/@a/video/2")])),
        ]);

        // "viral" matches no source, so the whole ordered chain runs (D5).
        var plan = await chain.DiscoverAsync(Request("viral"));

        Assert.Equal(2, plan.Candidates.Count);
        Assert.Equal(DiscoveryOrigin.Fresh, plan.Origin);
    }

    [Fact]
    public async Task SeveralBankedSources_ThePlanReportsTheOLDESTObservation()
    {
        var older = Observed.AddDays(-20);

        var chain = new ContentDiscoveryChain(
        [
            new StubSource("niche", new SourceDiscoveryResult(
                SourceOutcome.Ran, [Content("https://tiktok.com/@a/video/1")], DiscoveryOrigin.Bank, Observed)),
            new StubSource("references", new SourceDiscoveryResult(
                SourceOutcome.Ran, [Content("https://tiktok.com/@a/video/2")], DiscoveryOrigin.Bank, older)),
        ]);

        var plan = await chain.DiscoverAsync(Request("viral"));

        // The worst case is the honest one: "as old as" beats "as new as" when judging staleness.
        Assert.Equal(older, plan.BankObservedAtUtc);
    }

    [Fact]
    public async Task ABankedSourceThatContributedNothing_DoesNotMakeAFreshPlanLookStored()
    {
        var chain = new ContentDiscoveryChain(
        [
            new StubSource("niche", new SourceDiscoveryResult(
                SourceOutcome.Ran, [], DiscoveryOrigin.Bank, Observed)),
            new StubSource("references", new SourceDiscoveryResult(
                SourceOutcome.Ran, [Content("https://tiktok.com/@a/video/2")])),
        ]);

        var plan = await chain.DiscoverAsync(Request("viral"));

        Assert.Equal(DiscoveryOrigin.Fresh, plan.Origin);
        Assert.Null(plan.BankObservedAtUtc);
    }

    private static ContentDiscoveryRequest Request(string criterion = "niche") =>
        new(Guid.NewGuid(), Guid.NewGuid(), ["tiktok"], MaxResults: 10, SearchCriterion: criterion);

    private static DiscoveredContent Content(string url) =>
        new("t", "s", "tiktok", url, null,
            Views: 10, Likes: 1, Comments: 0, Shares: null, Saves: null,
            PublishedAtUtc: Observed, SourceHandle: "@a", SourceKey: "niche");

    private sealed class StubSource(string sourceKey, SourceDiscoveryResult result) : IContentDiscoverySource
    {
        public string SourceKey => sourceKey;

        public int Order => 1;

        public bool Supports(string searchCriterion) =>
            string.Equals(searchCriterion, sourceKey, StringComparison.OrdinalIgnoreCase);

        public Task<SourceDiscoveryResult> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct = default) =>
            Task.FromResult(result);
    }
}
