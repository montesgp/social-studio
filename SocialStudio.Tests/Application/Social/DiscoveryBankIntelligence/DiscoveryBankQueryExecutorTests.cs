using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.DiscoveryBankIntelligence;

namespace SocialStudio.Tests.Application.Social.DiscoveryBankIntelligence;

/// <summary>
/// THE ARCHITECTURAL RULE, expressed as tests: the SYSTEM does the arithmetic.
///
/// Every ranking, count and aggregate the AI Analysis tool reports is produced here — deterministically,
/// over the FULL bank — and never by the model. These tests are the contract that makes a "top 5" worth
/// acting on: exact ordering, exact tie-breaking, exact counts.
/// </summary>
public sealed class DiscoveryBankQueryExecutorTests
{
    private static readonly DateTimeOffset Observed = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static DiscoveryBankRow Row(
        string key,
        string author,
        long? views = null,
        long? likes = null,
        long? comments = null,
        long? shares = null,
        long? saves = null,
        string network = "tiktok",
        string[]? hashtags = null,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? observedAt = null,
        double? opportunityScore = null) =>
        new(
            CandidateKey: key,
            NetworkKey: network,
            Hashtags: hashtags ?? ["fitness"],
            ContentUrl: $"https://example.test/{key}",
            AuthorHandle: author,
            Title: $"Title {key}",
            PublishedAtUtc: publishedAt,
            ObservedAtUtc: observedAt ?? Observed,
            Views: views,
            Likes: likes,
            Comments: comments,
            Shares: shares,
            Saves: saves,
            OpportunityScore: opportunityScore);

    [Fact]
    public void TopByMetric_RanksByTheRequestedMetric_HighestFirst()
    {
        var rows = new[]
        {
            Row("a", "@alpha", views: 10),
            Row("b", "@beta", views: 900),
            Row("c", "@gamma", views: 300),
            Row("d", "@delta", views: 50),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.TopByMetric,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Sum,
            Limit: 2,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(["b", "c"], result.Rows.Select(x => x.CandidateKey));
        Assert.Equal(4, result.MatchedCount);
    }

    [Fact]
    public void TopByMetric_ByComments_UsesCommentsNotViews()
    {
        var rows = new[]
        {
            Row("a", "@alpha", views: 1_000_000, comments: 1),
            Row("b", "@beta", views: 10, comments: 999),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.TopByMetric,
            DiscoveryBankMetric.Comments,
            DiscoveryBankAggregate.Sum,
            Limit: 5,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(["b", "a"], result.Rows.Select(x => x.CandidateKey));
    }

    [Fact]
    public void TopByMetric_BreaksTiesOnCandidateKey_SoTheSameQuestionAlwaysGivesTheSameAnswer()
    {
        var rows = new[]
        {
            Row("zulu", "@z", views: 500),
            Row("alpha", "@a", views: 500),
            Row("mike", "@m", views: 500),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.TopByMetric,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Sum,
            Limit: 3,
            DiscoveryBankFilter.None);

        var first = DiscoveryBankQueryExecutor.Execute(spec, rows);
        var second = DiscoveryBankQueryExecutor.Execute(spec, rows.Reverse().ToArray());

        Assert.Equal(["alpha", "mike", "zulu"], first.Rows.Select(x => x.CandidateKey));
        Assert.Equal(first.Rows.Select(x => x.CandidateKey), second.Rows.Select(x => x.CandidateKey));
    }

    [Fact]
    public void TopByMetric_PutsCandidatesWithNoValueForTheMetricLast()
    {
        var rows = new[]
        {
            Row("missing", "@m"),
            Row("present", "@p", views: 1),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.TopByMetric,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Sum,
            Limit: 5,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(["present", "missing"], result.Rows.Select(x => x.CandidateKey));
    }

    [Fact]
    public void Engagement_SumsLikesCommentsSharesAndSaves()
    {
        var rows = new[]
        {
            Row("a", "@alpha", likes: 10, comments: 5, shares: 2, saves: 3),
            Row("b", "@beta", likes: 100),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.TopByMetric,
            DiscoveryBankMetric.Engagement,
            DiscoveryBankAggregate.Sum,
            Limit: 5,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(["b", "a"], result.Rows.Select(x => x.CandidateKey));
        Assert.Equal(20d, result.Rows.Single(x => x.CandidateKey == "a").MetricValue);
    }

    [Fact]
    public void Filters_NarrowByNetworkHashtagAuthorAndPublishedRange()
    {
        var rows = new[]
        {
            Row("keep", "@Alpha", views: 5, network: "tiktok", hashtags: ["fitness"], publishedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("wrong-network", "@Alpha", views: 5, network: "instagram", hashtags: ["fitness"], publishedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("wrong-hashtag", "@Alpha", views: 5, network: "tiktok", hashtags: ["cooking"], publishedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("wrong-author", "@Beta", views: 5, network: "tiktok", hashtags: ["fitness"], publishedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("too-old", "@Alpha", views: 5, network: "tiktok", hashtags: ["fitness"], publishedAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("too-new", "@Alpha", views: 5, network: "tiktok", hashtags: ["fitness"], publishedAt: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.CountCandidates,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Sum,
            Limit: 10,
            new DiscoveryBankFilter(
                NetworkKey: "TikTok",
                Hashtag: "Fitness",
                AuthorHandle: "alpha",
                PublishedFromUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                PublishedToUtc: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(1, result.MatchedCount);
    }

    [Fact]
    public void CountCandidates_CountsTheWholeMatchedSet_AndCitesNothingItDidNotRank()
    {
        var rows = Enumerable.Range(0, 37).Select(i => Row($"k{i:00}", "@a", views: i)).ToArray();

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.CountCandidates,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Sum,
            Limit: 5,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(37, result.MatchedCount);
        Assert.Empty(result.Rows);
    }

    [Theory]
    [InlineData(DiscoveryBankAggregate.Sum, 60d)]
    [InlineData(DiscoveryBankAggregate.Average, 20d)]
    [InlineData(DiscoveryBankAggregate.Maximum, 30d)]
    [InlineData(DiscoveryBankAggregate.Minimum, 10d)]
    public void AggregateMetric_ComputesExactly(DiscoveryBankAggregate aggregate, double expected)
    {
        var rows = new[]
        {
            Row("a", "@a", views: 10),
            Row("b", "@b", views: 20),
            Row("c", "@c", views: 30),
            Row("d", "@d"),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.AggregateMetric,
            DiscoveryBankMetric.Views,
            aggregate,
            Limit: 5,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(expected, result.AggregateValue);
    }

    [Fact]
    public void AggregateMetric_OverNoUsableValues_ReportsNoValueRatherThanZero()
    {
        var rows = new[] { Row("a", "@a"), Row("b", "@b") };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.AggregateMetric,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Average,
            Limit: 5,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Null(result.AggregateValue);
    }

    [Fact]
    public void BreakdownByNetwork_GroupsAndTotalsPerNetwork()
    {
        var rows = new[]
        {
            Row("a", "@a", views: 10, network: "tiktok"),
            Row("b", "@b", views: 20, network: "tiktok"),
            Row("c", "@c", views: 100, network: "instagram"),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.BreakdownByNetwork,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Sum,
            Limit: 10,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(["instagram", "tiktok"], result.Groups.Select(x => x.Key));
        Assert.Equal(1, result.Groups[0].CandidateCount);
        Assert.Equal(100d, result.Groups[0].MetricTotal);
        Assert.Equal(2, result.Groups[1].CandidateCount);
        Assert.Equal(30d, result.Groups[1].MetricTotal);
    }

    [Fact]
    public void BreakdownByAuthor_RanksAuthorsByMetricTotal_AndHonoursTheLimit()
    {
        var rows = new[]
        {
            Row("a1", "@alpha", views: 10),
            Row("a2", "@alpha", views: 20),
            Row("b1", "@beta", views: 100),
            Row("c1", "@gamma", views: 5),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.BreakdownByAuthor,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Sum,
            Limit: 2,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(["@beta", "@alpha"], result.Groups.Select(x => x.Key));
        Assert.Equal(4, result.MatchedCount);
        Assert.Equal(2, result.Groups.Count);
    }

    [Fact]
    public void ListRecent_OrdersByObservationDate_NewestFirst()
    {
        var rows = new[]
        {
            Row("old", "@a", observedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("new", "@b", observedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            Row("mid", "@c", observedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.ListRecent,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Sum,
            Limit: 2,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        Assert.Equal(["new", "mid"], result.Rows.Select(x => x.CandidateKey));
    }

    [Fact]
    public void EveryRankedRow_CarriesTheUrlAndHandleTheOperatorNeedsToVerifyIt()
    {
        var rows = new[] { Row("a", "@alpha", views: 10) };

        var spec = new DiscoveryBankQuerySpec(
            DiscoveryBankQueryKind.TopByMetric,
            DiscoveryBankMetric.Views,
            DiscoveryBankAggregate.Sum,
            Limit: 5,
            DiscoveryBankFilter.None);

        var result = DiscoveryBankQueryExecutor.Execute(spec, rows);

        var cited = Assert.Single(result.Rows);
        Assert.Equal("https://example.test/a", cited.ContentUrl);
        Assert.Equal("@alpha", cited.AuthorHandle);
    }
}
