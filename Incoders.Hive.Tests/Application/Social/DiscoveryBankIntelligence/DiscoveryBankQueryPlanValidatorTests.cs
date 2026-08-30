using Incoders.Hive.Application.Features.Social.DiscoveryBankIntelligence;
using Incoders.Hive.Domain.Social.DiscoveryBankIntelligence;

namespace Incoders.Hive.Tests.Application.Social.DiscoveryBankIntelligence;

/// <summary>
/// The model's plan is UNTRUSTED INPUT. Nothing it returns reaches the bank until it has been mapped
/// onto the explicitly enumerated query catalog, with typed parameters inside their allowed domain.
/// A plan that fails here executes NOTHING — the operator is told plainly what the tool can answer
/// instead of being handed an improvised answer.
/// </summary>
public sealed class DiscoveryBankQueryPlanValidatorTests
{
    private static readonly DiscoveryBankIntelligenceOptions Options = new();

    private static DiscoveryBankQueryPlan Plan(
        string? kind = "top_by_metric",
        string? metric = "views",
        string? aggregate = null,
        int? limit = 5,
        string? network = null,
        string? hashtag = null,
        string? author = null,
        string? from = null,
        string? to = null) =>
        new(kind, metric, aggregate, limit, network, hashtag, author, from, to, null, null);

    [Fact]
    public void MapsASupportedPlanOntoATypedSpec()
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(Plan(), Options);

        Assert.True(result.IsSuccess);
        Assert.Equal(DiscoveryBankQueryKind.TopByMetric, result.Value.Kind);
        Assert.Equal(DiscoveryBankMetric.Views, result.Value.Metric);
        Assert.Equal(5, result.Value.Limit);
    }

    [Theory]
    [InlineData("TOP_BY_METRIC")]
    [InlineData("top-by-metric")]
    [InlineData("  topByMetric ")]
    public void AcceptsTheCatalogKeyInAnyReasonableSpelling(string kind)
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(Plan(kind: kind), Options);

        Assert.True(result.IsSuccess);
        Assert.Equal(DiscoveryBankQueryKind.TopByMetric, result.Value.Kind);
    }

    [Theory]
    [InlineData("rank_by_vibes")]
    [InlineData("SELECT * FROM discovered_candidates")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsAQueryKindThatIsNotInTheCatalog(string? kind)
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(Plan(kind: kind), Options);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.UnsupportedQuestion.Code, result.Error.Code);
    }

    [Fact]
    public void RejectsAMetricThatIsNotInTheAllowedDomain()
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(Plan(metric: "virality"), Options);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.UnsupportedQuestion.Code, result.Error.Code);
    }

    [Fact]
    public void RejectsANetworkOutsideTheKnownNetworks()
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(Plan(network: "myspace"), Options);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.InvalidQueryParameter.Code, result.Error.Code);
    }

    [Fact]
    public void RejectsADateItCannotParse()
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(Plan(from: "last summer"), Options);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.InvalidQueryParameter.Code, result.Error.Code);
    }

    [Fact]
    public void RejectsAnInvertedDateRange()
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(
            Plan(from: "2026-08-01T00:00:00Z", to: "2026-01-01T00:00:00Z"),
            Options);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.InvalidQueryParameter.Code, result.Error.Code);
    }

    [Fact]
    public void RejectsAnAggregateFunctionOutsideTheAllowedDomain()
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(
            Plan(kind: "aggregate_metric", aggregate: "median"),
            Options);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.UnsupportedQuestion.Code, result.Error.Code);
    }

    [Fact]
    public void RequiresAnAggregateFunctionForAnAggregateQuery()
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(
            Plan(kind: "aggregate_metric", aggregate: null),
            Options);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.UnsupportedQuestion.Code, result.Error.Code);
    }

    [Fact]
    public void ClampsAnOversizedLimitToTheConfiguredRowCap()
    {
        var options = new DiscoveryBankIntelligenceOptions { MaxRowsToModel = 10 };

        var result = DiscoveryBankQueryPlanValidator.Validate(Plan(limit: 5_000), options);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value.Limit);
    }

    [Fact]
    public void ClampsANonPositiveLimitToOne()
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(Plan(limit: 0), Options);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Limit);
    }

    [Fact]
    public void DefaultsAMissingLimitRatherThanFailing()
    {
        var result = DiscoveryBankQueryPlanValidator.Validate(Plan(limit: null), Options);

        Assert.True(result.IsSuccess);
        Assert.Equal(Options.DefaultLimit, result.Value.Limit);
    }

    [Fact]
    public void RejectsThePlanWhenTheModelItselfSaysTheQuestionIsUnsupported()
    {
        var plan = new DiscoveryBankQueryPlan(
            "top_by_metric", "views", null, 5, null, null, null, null, null,
            Unsupported: "The operator asked about published post performance, which is not in the bank.",
            Interpretation: null);

        var result = DiscoveryBankQueryPlanValidator.Validate(plan, Options);

        Assert.True(result.IsFailure);
        Assert.Equal(DiscoveryBankErrors.UnsupportedQuestion.Code, result.Error.Code);
    }

    [Fact]
    public void TheCatalogNamesEverySupportedQueryInPlainEnglish()
    {
        Assert.Equal(
            Enum.GetValues<DiscoveryBankQueryKind>().Length,
            DiscoveryBankQueryCatalog.Entries.Count);

        Assert.All(DiscoveryBankQueryCatalog.Entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Key));
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
            Assert.False(string.IsNullOrWhiteSpace(entry.ExampleQuestion));
        });
    }
}
