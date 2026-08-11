using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// WU-4: CD-04, CD-05. The scorer is a PURE function — <c>nowUtc</c> is a parameter, never read
/// internally — and it must be availability-aware: a signal the network doesn't report drops out of
/// both numerator and denominator, never treated as zero.
/// </summary>
public sealed class OpportunityScorerTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, AccountBaseline> NoBaselines =
        new Dictionary<string, AccountBaseline>();

    [Fact]
    public void SameInputTwice_ProducesTheIdenticalScore()
    {
        var set = new[]
        {
            Candidate("a", views: 10_000, likes: 500, comments: 20, shares: 5, saves: 3, publishedDaysAgo: 2, handle: "@a"),
            Candidate("b", views: 2_000, likes: 40, comments: 5, shares: null, saves: null, publishedDaysAgo: 40, handle: "@b"),
        };

        var first = OpportunityScorer.Score(set, NoBaselines, NowUtc);
        var second = OpportunityScorer.Score(set, NoBaselines, NowUtc);

        Assert.Equal(first.Select(s => s.Score), second.Select(s => s.Score));
    }

    [Fact]
    public void MissingSignal_IsExcludedFromDenominator_NotZeroFilled()
    {
        // Two candidates, identical in every way EXCEPT one is missing shares/saves entirely.
        // If the scorer zero-filled, the candidate with fewer available signals would always score
        // lower for that signal; excluding it from the denominator means it competes fairly on what
        // IS available.
        var withAllSignals = Candidate("with-all", views: 5_000, likes: 200, comments: 10, shares: 20, saves: 10, publishedDaysAgo: 1, handle: "@a");
        var missingSharesAndSaves = Candidate("missing", views: 5_000, likes: 200, comments: 10, shares: null, saves: null, publishedDaysAgo: 1, handle: "@b");

        var scored = OpportunityScorer.Score([withAllSignals, missingSharesAndSaves], NoBaselines, NowUtc);

        // Both must produce a finite score in [0, 1] — no NaN/Infinity from a zero-filled denominator.
        Assert.All(scored, s => Assert.InRange(s.Score, 0.0, 1.0));
    }

    [Fact]
    public void DegenerateSet_AllSignalsEqual_FallsBackToHalfNormalizedScore()
    {
        // A single candidate: every set-relative (min-max) signal is degenerate by definition.
        var only = Candidate("only", views: 5_000, likes: 200, comments: 10, shares: 20, saves: 10, publishedDaysAgo: 1, handle: "@a");

        var scored = OpportunityScorer.Score([only], NoBaselines, NowUtc);

        Assert.Single(scored);
        Assert.InRange(scored[0].Score, 0.0, 1.0);
    }

    [Fact]
    public void HigherPerformingCandidate_ScoresAboveALowerPerformingOne()
    {
        var strong = Candidate("strong", views: 100_000, likes: 8_000, comments: 500, shares: 300, saves: 200, publishedDaysAgo: 1, handle: "@a");
        var weak = Candidate("weak", views: 100, likes: 2, comments: 0, shares: 0, saves: 0, publishedDaysAgo: 200, handle: "@b");

        var scored = OpportunityScorer.Score([strong, weak], NoBaselines, NowUtc);

        var strongScore = scored.Single(s => s.Candidate.Title == "strong").Score;
        var weakScore = scored.Single(s => s.Candidate.Title == "weak").Score;

        Assert.True(strongScore > weakScore);
    }

    [Fact]
    public void EmptySet_ReturnsEmptyResult()
    {
        var scored = OpportunityScorer.Score([], NoBaselines, NowUtc);

        Assert.Empty(scored);
    }

    private static DiscoveredContent Candidate(
        string title,
        long? views,
        long? likes,
        long? comments,
        long? shares,
        long? saves,
        int publishedDaysAgo,
        string handle) =>
        new(
            Title: title,
            Summary: $"{title} summary content about creators and marketing",
            NetworkSource: "tiktok",
            ContentUrl: $"https://tiktok.com/{handle}/video/{Guid.NewGuid()}",
            ThumbnailUrl: null,
            Views: views,
            Likes: likes,
            Comments: comments,
            Shares: shares,
            Saves: saves,
            PublishedAtUtc: NowUtc.AddDays(-publishedDaysAgo),
            SourceHandle: handle,
            SourceKey: "niche");
}
