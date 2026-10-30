using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// WU-4: CD-06. Trending/Proven/Undetermined from a single snapshot. The null-publish-date branch
/// must win FIRST and unconditionally — never defaulted to Proven/Trending even when other fields
/// look strong.
/// </summary>
public sealed class TrendingProvenClassifierTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, AccountBaseline> NoBaselines =
        new Dictionary<string, AccountBaseline>();

    [Fact]
    public void NullPublishedAt_IsUndetermined_First_EvenWithHighMetrics()
    {
        var candidate = Candidate(views: 10_000_000, publishedAtUtc: null);

        var result = TrendingProvenClassifier.Classify(candidate, [candidate], NoBaselines, NowUtc);

        Assert.Equal(ContentClassification.Undetermined, result);
    }

    [Fact]
    public void Recent_HighVelocity_IsTrending()
    {
        var baseline = new AccountBaseline("tiktok", "@a", MedianViews: 700, MedianEngagement: 10, PostCount: 20, ComputedAt: NowUtc);
        var baselines = new Dictionary<string, AccountBaseline> { [AccountBaseline.KeyFor("tiktok", "@a")] = baseline };

        // Published 1 day ago with far more views/day than the baseline implies.
        var candidate = Candidate(views: 500_000, publishedAtUtc: NowUtc.AddDays(-1));

        var result = TrendingProvenClassifier.Classify(candidate, [candidate], baselines, NowUtc);

        Assert.Equal(ContentClassification.Trending, result);
    }

    [Fact]
    public void Old_ConsistentlyHighPerformer_IsProven()
    {
        var baseline = new AccountBaseline("tiktok", "@a", MedianViews: 1_000, MedianEngagement: 10, PostCount: 20, ComputedAt: NowUtc);
        var baselines = new Dictionary<string, AccountBaseline> { [AccountBaseline.KeyFor("tiktok", "@a")] = baseline };

        var candidate = Candidate(views: 50_000, publishedAtUtc: NowUtc.AddDays(-120));

        var result = TrendingProvenClassifier.Classify(candidate, [candidate], baselines, NowUtc);

        Assert.Equal(ContentClassification.Proven, result);
    }

    [Fact]
    public void OldButUnremarkable_NoBaseline_FallsBackToSetMedian_StaysUndetermined()
    {
        var strong = Candidate(views: 10_000, publishedAtUtc: NowUtc.AddDays(-100));
        var weak = Candidate(views: 9_000, publishedAtUtc: NowUtc.AddDays(-100));

        var result = TrendingProvenClassifier.Classify(weak, [strong, weak], NoBaselines, NowUtc);

        Assert.Equal(ContentClassification.Undetermined, result);
    }

    private static DiscoveredContent Candidate(long? views, DateTimeOffset? publishedAtUtc) =>
        new("t", "s", "tiktok", $"https://tiktok.com/@a/video/{Guid.NewGuid()}", null,
            views, 100, 10, 5, 2, publishedAtUtc, "@a", "niche");
}
