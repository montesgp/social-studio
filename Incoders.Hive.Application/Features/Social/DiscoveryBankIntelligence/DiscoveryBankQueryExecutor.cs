using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

namespace Incoders.Hive.Application.Features.Social.DiscoveryBankIntelligence;

/// <summary>
/// THE ARITHMETIC. Every number the AI Analysis tool reports is produced here.
///
/// Why this is not the model's job: an LLM asked to rank a table returns a confident ordering that is
/// frequently wrong, and it degrades silently — nothing in the answer says "I only looked at 40 of your
/// 4,000 rows". This runs over the FULL matched set, is exact, is deterministic down to tie-breaking, and
/// costs nothing. The model's contribution is choosing WHICH of these to run and describing the outcome.
///
/// Pure and static on purpose: no clock, no repository, no I/O. The same spec over the same rows always
/// yields the same result, which is what makes an answer worth acting on.
/// </summary>
public static class DiscoveryBankQueryExecutor
{
    public static DiscoveryBankQueryResult Execute(
        DiscoveryBankQuerySpec spec,
        IReadOnlyList<DiscoveryBankRow> bank)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(bank);

        var matched = bank.Where(row => Matches(row, spec.Filter)).ToList();

        return spec.Kind switch
        {
            DiscoveryBankQueryKind.TopByMetric => new DiscoveryBankQueryResult(
                spec,
                matched.Count,
                RankByMetric(matched, spec.Metric).Take(spec.Limit).ToList(),
                [],
                null),

            DiscoveryBankQueryKind.CountCandidates => new DiscoveryBankQueryResult(
                spec,
                matched.Count,
                [],
                [],
                matched.Count),

            DiscoveryBankQueryKind.AggregateMetric => new DiscoveryBankQueryResult(
                spec,
                matched.Count,
                [],
                [],
                Aggregate(matched, spec.Metric, spec.Aggregate)),

            DiscoveryBankQueryKind.BreakdownByNetwork => new DiscoveryBankQueryResult(
                spec,
                matched.Count,
                [],
                GroupBy(matched, spec.Metric, row => row.NetworkKey, orderByTotal: false).Take(spec.Limit).ToList(),
                null),

            DiscoveryBankQueryKind.BreakdownByAuthor => new DiscoveryBankQueryResult(
                spec,
                matched.Count,
                [],
                GroupBy(matched, spec.Metric, row => row.AuthorHandle, orderByTotal: true).Take(spec.Limit).ToList(),
                null),

            DiscoveryBankQueryKind.ListRecent => new DiscoveryBankQueryResult(
                spec,
                matched.Count,
                matched
                    .OrderByDescending(row => row.ObservedAtUtc)
                    .ThenBy(row => row.CandidateKey, StringComparer.Ordinal)
                    .Take(spec.Limit)
                    .Select(row => Project(row, spec.Metric))
                    .ToList(),
                [],
                null),

            _ => new DiscoveryBankQueryResult(spec, matched.Count, [], [], null),
        };
    }

    // ── filtering ────────────────────────────────────────────────────────────

    private static bool Matches(DiscoveryBankRow row, DiscoveryBankFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.NetworkKey)
            && !string.Equals(row.NetworkKey.Trim(), filter.NetworkKey.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Hashtag)
            && !row.Hashtags.Any(tag => string.Equals(NormalizeHashtag(tag), NormalizeHashtag(filter.Hashtag), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.AuthorHandle)
            && !string.Equals(NormalizeHandle(row.AuthorHandle), NormalizeHandle(filter.AuthorHandle), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A candidate with no publication date cannot be shown to fall inside a date range, so a range
        // filter excludes it. Including it would quietly widen the answer beyond what was asked.
        if (filter.PublishedFromUtc is { } from && (row.PublishedAtUtc is null || row.PublishedAtUtc < from))
        {
            return false;
        }

        if (filter.PublishedToUtc is { } to && (row.PublishedAtUtc is null || row.PublishedAtUtc > to))
        {
            return false;
        }

        return true;
    }

    /// <summary>Handles are compared without the leading <c>@</c> — operators type it inconsistently.</summary>
    private static string NormalizeHandle(string value) => value.Trim().TrimStart('@');

    /// <summary>Hashtags are compared without the leading <c>#</c>, matching how discovery banks them.</summary>
    private static string NormalizeHashtag(string value) => value.Trim().TrimStart('#');

    // ── ranking ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Highest metric first; candidates with NO value for the metric go last (never as zero, which would
    /// claim knowledge the bank does not have); ties broken on the candidate key so the same question
    /// always produces the same order regardless of how the rows arrived.
    /// </summary>
    private static IEnumerable<DiscoveryBankResultRow> RankByMetric(
        IReadOnlyList<DiscoveryBankRow> rows,
        DiscoveryBankMetric metric) =>
        rows
            .Select(row => (Row: row, Value: DiscoveryBankMetricReader.Read(row, metric)))
            .OrderBy(entry => entry.Value is null ? 1 : 0)
            .ThenByDescending(entry => entry.Value ?? double.MinValue)
            .ThenBy(entry => entry.Row.CandidateKey, StringComparer.Ordinal)
            .Select(entry => Project(entry.Row, metric));

    private static DiscoveryBankResultRow Project(DiscoveryBankRow row, DiscoveryBankMetric metric) => new(
        row.CandidateKey,
        row.NetworkKey,
        row.ContentUrl,
        row.AuthorHandle,
        row.Title,
        row.Hashtags,
        row.PublishedAtUtc,
        row.ObservedAtUtc,
        row.Views,
        row.Likes,
        row.Comments,
        row.Shares,
        row.Saves,
        row.OpportunityScore,
        DiscoveryBankMetricReader.Read(row, metric));

    // ── aggregates ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>null</c> when no matched candidate carries the metric. "No usable value" and "zero" are
    /// different answers and the tool must not conflate them.
    /// </summary>
    private static double? Aggregate(
        IReadOnlyList<DiscoveryBankRow> rows,
        DiscoveryBankMetric metric,
        DiscoveryBankAggregate aggregate)
    {
        var values = rows
            .Select(row => DiscoveryBankMetricReader.Read(row, metric))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToList();

        if (values.Count == 0)
        {
            return null;
        }

        return aggregate switch
        {
            DiscoveryBankAggregate.Sum => values.Sum(),
            DiscoveryBankAggregate.Average => values.Average(),
            DiscoveryBankAggregate.Maximum => values.Max(),
            DiscoveryBankAggregate.Minimum => values.Min(),
            _ => null,
        };
    }

    // ── breakdowns ───────────────────────────────────────────────────────────

    private static IEnumerable<DiscoveryBankResultGroup> GroupBy(
        IReadOnlyList<DiscoveryBankRow> rows,
        DiscoveryBankMetric metric,
        Func<DiscoveryBankRow, string> keySelector,
        bool orderByTotal)
    {
        var groups = rows
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DiscoveryBankResultGroup(
                group.Key,
                group.Count(),
                group.Sum(row => DiscoveryBankMetricReader.Read(row, metric) ?? 0d)));

        // Networks read best alphabetically (a stable, scannable list); authors are a ranking, so they
        // lead with the biggest contributor. Both fall back to the key so the order is total.
        return orderByTotal
            ? groups.OrderByDescending(group => group.MetricTotal).ThenBy(group => group.Key, StringComparer.Ordinal)
            : groups.OrderBy(group => group.Key, StringComparer.Ordinal);
    }
}
