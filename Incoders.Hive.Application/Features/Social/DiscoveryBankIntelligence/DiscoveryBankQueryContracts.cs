using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

namespace Incoders.Hive.Application.Features.Social.DiscoveryBankIntelligence;

/// <summary>
/// The COMPLETE set of questions the AI Analysis tool can answer about the discovery candidate bank.
///
/// This enum is the whole safety story. The model never writes a query — it picks one of these names and
/// supplies typed parameters, and anything outside the set is refused before a single row is read. New
/// capability means a new member here plus a deterministic implementation and its tests, never a looser
/// prompt.
/// </summary>
public enum DiscoveryBankQueryKind
{
    /// <summary>The N strongest candidates by one metric, highest first.</summary>
    TopByMetric = 0,

    /// <summary>How many candidates match the filters.</summary>
    CountCandidates = 1,

    /// <summary>Sum / average / maximum / minimum of one metric across the matched set.</summary>
    AggregateMetric = 2,

    /// <summary>Candidate count and metric total per network.</summary>
    BreakdownByNetwork = 3,

    /// <summary>The authors with the highest metric total, and how many candidates each contributed.</summary>
    BreakdownByAuthor = 4,

    /// <summary>The most recently observed candidates, newest first.</summary>
    ListRecent = 5,
}

/// <summary>The metrics the bank actually stores. Nothing here is derived from a model's opinion.</summary>
public enum DiscoveryBankMetric
{
    Views = 0,
    Likes = 1,
    Comments = 2,
    Shares = 3,
    Saves = 4,

    /// <summary>Likes + comments + shares + saves. Absent components count as zero.</summary>
    Engagement = 5,

    /// <summary>The opportunity score discovery already computed and stored.</summary>
    OpportunityScore = 6,
}

public enum DiscoveryBankAggregate
{
    Sum = 0,
    Average = 1,
    Maximum = 2,
    Minimum = 3,
}

/// <summary>
/// The typed, validated narrowing of the bank a query runs over. Every field is optional; every field
/// that is present has already been checked against its allowed domain.
/// </summary>
public sealed record DiscoveryBankFilter(
    string? NetworkKey,
    string? Hashtag,
    string? AuthorHandle,
    DateTimeOffset? PublishedFromUtc,
    DateTimeOffset? PublishedToUtc)
{
    public static readonly DiscoveryBankFilter None = new(null, null, null, null, null);

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(NetworkKey)
        && string.IsNullOrWhiteSpace(Hashtag)
        && string.IsNullOrWhiteSpace(AuthorHandle)
        && PublishedFromUtc is null
        && PublishedToUtc is null;
}

/// <summary>A validated, executable query. Only <c>DiscoveryBankQueryPlanValidator</c> produces one.</summary>
public sealed record DiscoveryBankQuerySpec(
    DiscoveryBankQueryKind Kind,
    DiscoveryBankMetric Metric,
    DiscoveryBankAggregate Aggregate,
    int Limit,
    DiscoveryBankFilter Filter);

/// <summary>
/// One candidate a query ranked or listed. <c>MetricValue</c> is the value of the query's metric for this
/// candidate, or <c>null</c> when the bank stores none.
/// </summary>
public sealed record DiscoveryBankResultRow(
    string CandidateKey,
    string NetworkKey,
    string ContentUrl,
    string AuthorHandle,
    string Title,
    IReadOnlyList<string> Hashtags,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset ObservedAtUtc,
    long? Views,
    long? Likes,
    long? Comments,
    long? Shares,
    long? Saves,
    double? OpportunityScore,
    double? MetricValue);

/// <summary>One bucket of a breakdown query.</summary>
public sealed record DiscoveryBankResultGroup(
    string Key,
    int CandidateCount,
    double MetricTotal);

/// <summary>
/// What the system computed. The model sees a bounded slice of this and nothing else — never the bank.
/// </summary>
/// <remarks>
/// <c>MatchedCount</c> is how many candidates matched the filters over the FULL bank, NOT how many rows
/// were returned — the difference is exactly what stops a capped slice from reading as the whole truth.
/// </remarks>
public sealed record DiscoveryBankQueryResult(
    DiscoveryBankQuerySpec Spec,
    int MatchedCount,
    IReadOnlyList<DiscoveryBankResultRow> Rows,
    IReadOnlyList<DiscoveryBankResultGroup> Groups,
    double? AggregateValue);

/// <summary>
/// The untrusted plan the model returns. Every member is a nullable primitive on purpose: this record is
/// deserialized straight from model output, so it must be able to represent nonsense without throwing —
/// rejection is the validator's job, not the deserializer's.
///
/// <c>Unsupported</c> is set by the model when the question maps to nothing in the catalog — any value
/// refuses the plan. <c>Interpretation</c> is a one-line restatement of the question, shown to the
/// operator so they can check how it was read.
/// </summary>
public sealed record DiscoveryBankQueryPlan(
    string? QueryKind,
    string? Metric,
    string? Aggregate,
    int? Limit,
    string? NetworkKey,
    string? Hashtag,
    string? AuthorHandle,
    string? PublishedFromUtc,
    string? PublishedToUtc,
    string? Unsupported,
    string? Interpretation);

/// <summary>The model's short narration over the computed rows. Prose only — it carries no numbers of its own.</summary>
public sealed record DiscoveryBankNarration(string Answer);

/// <summary>
/// The catalog rendered for humans and for the planning prompt. One entry per
/// <see cref="DiscoveryBankQueryKind"/>, enforced by test.
/// </summary>
public sealed record DiscoveryBankCatalogEntry(
    DiscoveryBankQueryKind Kind,
    string Key,
    string Label,
    string Description,
    string ExampleQuestion);

public static class DiscoveryBankQueryCatalog
{
    public static IReadOnlyList<DiscoveryBankCatalogEntry> Entries { get; } =
    [
        new(
            DiscoveryBankQueryKind.TopByMetric,
            "top_by_metric",
            "Top candidates by a metric",
            "Ranks the matching candidates by one stored metric (views, likes, comments, shares, saves, engagement, opportunity score) and returns the strongest ones.",
            "Top 5 by views"),
        new(
            DiscoveryBankQueryKind.CountCandidates,
            "count_candidates",
            "How many candidates match",
            "Counts the candidates matching the filters, across the whole bank.",
            "How many TikTok candidates do we have?"),
        new(
            DiscoveryBankQueryKind.AggregateMetric,
            "aggregate_metric",
            "Sum, average, maximum or minimum of a metric",
            "Computes one aggregate of one metric over the matching candidates.",
            "What is the average view count?"),
        new(
            DiscoveryBankQueryKind.BreakdownByNetwork,
            "breakdown_by_network",
            "Breakdown by network",
            "Groups the matching candidates by network and reports the count and metric total of each.",
            "How is the bank split across networks?"),
        new(
            DiscoveryBankQueryKind.BreakdownByAuthor,
            "breakdown_by_author",
            "Breakdown by author",
            "Groups the matching candidates by author handle and ranks the authors by metric total.",
            "Which accounts contribute the most views?"),
        new(
            DiscoveryBankQueryKind.ListRecent,
            "list_recent",
            "Most recently discovered candidates",
            "Lists the matching candidates by observation date, newest first.",
            "What did discovery find most recently?"),
    ];

    public static DiscoveryBankCatalogEntry For(DiscoveryBankQueryKind kind) =>
        Entries.First(entry => entry.Kind == kind);
}

/// <summary>Reads one metric off a banked row. The single place metric semantics are defined.</summary>
internal static class DiscoveryBankMetricReader
{
    internal static double? Read(DiscoveryBankRow row, DiscoveryBankMetric metric) => metric switch
    {
        DiscoveryBankMetric.Views => row.Views,
        DiscoveryBankMetric.Likes => row.Likes,
        DiscoveryBankMetric.Comments => row.Comments,
        DiscoveryBankMetric.Shares => row.Shares,
        DiscoveryBankMetric.Saves => row.Saves,
        DiscoveryBankMetric.OpportunityScore => row.OpportunityScore,
        DiscoveryBankMetric.Engagement => ReadEngagement(row),
        _ => null,
    };

    /// <summary>
    /// Engagement is the sum of the interaction counters the bank has. A row with none of them has no
    /// engagement value at all rather than an engagement of zero — zero would rank it alongside a
    /// genuinely unengaging post instead of admitting the number is unknown.
    /// </summary>
    private static double? ReadEngagement(DiscoveryBankRow row)
    {
        if (row.Likes is null && row.Comments is null && row.Shares is null && row.Saves is null)
        {
            return null;
        }

        return (row.Likes ?? 0) + (row.Comments ?? 0) + (row.Shares ?? 0) + (row.Saves ?? 0);
    }
}
