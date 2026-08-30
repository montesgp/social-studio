using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

namespace Incoders.Hive.Tests.Application.Abstractions.Social;

/// <summary>
/// WU-1: <see cref="ContentDiscoveryPlan"/> is the port's return shape. It carries three distinct
/// reporting channels — shortfall, failed sources, and (D12/CD-13) unconfigured sources — so the
/// operator can tell "the provider is down" apart from "nothing was ever configured".
/// </summary>
public sealed class ContentDiscoveryPlanTests
{
    [Fact]
    public void ContentDiscoveryPlan_HasFourFields_CandidatesShortfallFailedUnconfigured()
    {
        var candidate = new DiscoveredContent(
            "title", "summary", "tiktok", "https://tiktok.com/@a/video/1", null,
            Views: 100, Likes: 10, Comments: 1, Shares: null, Saves: null,
            PublishedAtUtc: DateTimeOffset.UtcNow, SourceHandle: "@a", SourceKey: "references");

        var plan = new ContentDiscoveryPlan(
            Candidates: [candidate],
            Shortfall: 3,
            FailedSourceKeys: ["niche"],
            UnconfiguredSourceKeys: ["own"]);

        Assert.Single(plan.Candidates);
        Assert.Equal(3, plan.Shortfall);
        Assert.Equal(["niche"], plan.FailedSourceKeys);
        Assert.Equal(["own"], plan.UnconfiguredSourceKeys);
    }
}
