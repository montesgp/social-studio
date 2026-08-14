using System.Globalization;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.DiscoveryBankIntelligence;

namespace Incoders.Hive.Application.Features.Social.DiscoveryBankIntelligence;

/// <summary>
/// The trust boundary. A <see cref="DiscoveryBankQueryPlan"/> is model output — untrusted input, no
/// different from a request body — and this is the only door it can pass through to reach the bank.
///
/// What that buys: the model can never express a query, only NAME one from the catalog and supply typed
/// parameters. There is no dynamic SQL, no predicate assembled from a string, and no way for a prompt
/// injection in a scraped title to widen what runs. A plan that fails here executes NOTHING — the caller
/// tells the operator what the tool can answer instead of improvising.
///
/// Two error codes, and the distinction is deliberate:
/// <list type="bullet">
/// <item><description><c>UnsupportedQuestion</c> — the plan named a query, metric or aggregate outside the
/// enumerated domain. The QUESTION cannot be answered by this tool at all.</description></item>
/// <item><description><c>InvalidQueryParameter</c> — the query is supported but a free-text parameter is
/// unusable (an unknown network, an unparseable date, an inverted range).</description></item>
/// </list>
/// </summary>
public static class DiscoveryBankQueryPlanValidator
{
    /// <summary>
    /// The networks a filter may name. Mirrors the discovery workflow's supported networks: a filter for
    /// anything else would match nothing, and silently returning "0 candidates" reads as a fact about the
    /// bank rather than a typo in the plan.
    /// </summary>
    private static readonly string[] KnownNetworkKeys = ["instagram", "tiktok", "youtube"];

    /// <summary>Longest accepted free-text filter value. A handle or hashtag is never longer than this.</summary>
    private const int MaxFilterTextLength = 100;

    public static Result<DiscoveryBankQuerySpec> Validate(
        DiscoveryBankQueryPlan? plan,
        DiscoveryBankIntelligenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (plan is null)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(DiscoveryBankErrors.UnsupportedQuestion);
        }

        // The model's own refusal is honoured verbatim. It is the cheapest possible rejection and the one
        // most likely to be right about a question the catalog genuinely does not cover.
        if (!string.IsNullOrWhiteSpace(plan.Unsupported))
        {
            return Result.Failure<DiscoveryBankQuerySpec>(DiscoveryBankErrors.UnsupportedQuestion);
        }

        var kind = ResolveKind(plan.QueryKind);
        if (kind is null)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(DiscoveryBankErrors.UnsupportedQuestion);
        }

        var metric = ResolveMetric(plan.Metric);
        if (metric is null)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(DiscoveryBankErrors.UnsupportedQuestion);
        }

        var aggregate = ResolveAggregate(plan.Aggregate);
        if (aggregate is null)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(DiscoveryBankErrors.UnsupportedQuestion);
        }

        // An aggregate query with no named function is not a defaultable omission: "sum of views" and
        // "average views" are different answers, and picking one for the operator would be a guess.
        if (kind == DiscoveryBankQueryKind.AggregateMetric && string.IsNullOrWhiteSpace(plan.Aggregate))
        {
            return Result.Failure<DiscoveryBankQuerySpec>(DiscoveryBankErrors.UnsupportedQuestion);
        }

        var network = ResolveNetwork(plan.NetworkKey);
        if (network.IsFailure)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(network.Error);
        }

        var hashtag = ResolveFilterText(plan.Hashtag);
        if (hashtag.IsFailure)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(hashtag.Error);
        }

        var author = ResolveFilterText(plan.AuthorHandle);
        if (author.IsFailure)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(author.Error);
        }

        var from = ResolveInstant(plan.PublishedFromUtc);
        if (from.IsFailure)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(from.Error);
        }

        var to = ResolveInstant(plan.PublishedToUtc);
        if (to.IsFailure)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(to.Error);
        }

        if (from.Value is { } start && to.Value is { } end && start > end)
        {
            return Result.Failure<DiscoveryBankQuerySpec>(DiscoveryBankErrors.InvalidQueryParameter);
        }

        return Result.Success(new DiscoveryBankQuerySpec(
            kind.Value,
            metric.Value,
            aggregate.Value,
            ResolveLimit(plan.Limit, options),
            new DiscoveryBankFilter(network.Value, hashtag.Value, author.Value, from.Value, to.Value)));
    }

    /// <summary>
    /// Clamped, never rejected. An oversized limit is the model being enthusiastic about a legitimate
    /// question, not an attack — and the response reports the true matched count next to the returned
    /// rows, so a clamp is visible rather than a silent truncation.
    /// </summary>
    private static int ResolveLimit(int? requested, DiscoveryBankIntelligenceOptions options)
    {
        var cap = Math.Max(1, options.MaxRowsToModel);

        if (requested is not { } value)
        {
            return Math.Clamp(options.DefaultLimit, 1, cap);
        }

        return Math.Clamp(value, 1, cap);
    }

    private static DiscoveryBankQueryKind? ResolveKind(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return null;
        }

        foreach (var entry in DiscoveryBankQueryCatalog.Entries)
        {
            if (normalized == Normalize(entry.Key) || normalized == Normalize(entry.Kind.ToString()))
            {
                return entry.Kind;
            }
        }

        return null;
    }

    /// <summary>
    /// A missing metric is legitimate — "how many candidates do we have?" names none — and defaults to
    /// views, the bank's primary signal. A metric that is PRESENT but unknown is a refusal: answering a
    /// question about "virality" with a view count would be a substitution, not an answer.
    /// </summary>
    private static DiscoveryBankMetric? ResolveMetric(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return DiscoveryBankMetric.Views;
        }

        return normalized switch
        {
            "views" or "view" or "viewcount" or "plays" => DiscoveryBankMetric.Views,
            "likes" or "like" or "likecount" => DiscoveryBankMetric.Likes,
            "comments" or "comment" or "commentcount" => DiscoveryBankMetric.Comments,
            "shares" or "share" or "sharecount" => DiscoveryBankMetric.Shares,
            "saves" or "save" or "savecount" or "bookmarks" => DiscoveryBankMetric.Saves,
            "engagement" or "engagementtotal" or "interactions" => DiscoveryBankMetric.Engagement,
            "opportunityscore" or "opportunity" or "score" => DiscoveryBankMetric.OpportunityScore,
            _ => null,
        };
    }

    private static DiscoveryBankAggregate? ResolveAggregate(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return DiscoveryBankAggregate.Sum;
        }

        return normalized switch
        {
            "sum" or "total" => DiscoveryBankAggregate.Sum,
            "average" or "avg" or "mean" => DiscoveryBankAggregate.Average,
            "maximum" or "max" or "highest" => DiscoveryBankAggregate.Maximum,
            "minimum" or "min" or "lowest" => DiscoveryBankAggregate.Minimum,
            _ => null,
        };
    }

    private static Result<string?> ResolveNetwork(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Success<string?>(null);
        }

        var match = KnownNetworkKeys.FirstOrDefault(known =>
            string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase));

        return match is null
            ? Result.Failure<string?>(DiscoveryBankErrors.InvalidQueryParameter)
            : Result.Success<string?>(match);
    }

    private static Result<string?> ResolveFilterText(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Success<string?>(null);
        }

        return trimmed.Length > MaxFilterTextLength
            ? Result.Failure<string?>(DiscoveryBankErrors.InvalidQueryParameter)
            : Result.Success<string?>(trimmed);
    }

    /// <summary>
    /// Dates must arrive as machine-readable instants. Natural language ("last summer") is refused rather
    /// than interpreted: a date range the operator did not choose silently changes what the answer means.
    /// </summary>
    private static Result<DateTimeOffset?> ResolveInstant(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Success<DateTimeOffset?>(null);
        }

        return DateTimeOffset.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? Result.Success<DateTimeOffset?>(parsed)
            : Result.Failure<DateTimeOffset?>(DiscoveryBankErrors.InvalidQueryParameter);
    }

    /// <summary>
    /// Case, spacing and separator style are the model's business, not ours: <c>top_by_metric</c>,
    /// <c>top-by-metric</c> and <c>topByMetric</c> are the same intent and all three are accepted. What is
    /// NOT accepted is anything that does not reduce to a catalog key.
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
