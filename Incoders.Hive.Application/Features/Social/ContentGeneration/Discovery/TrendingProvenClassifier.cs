using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>
/// Three-valued classification for a discovered candidate (CD-06): <c>Undetermined</c>, <c>Trending</c>,
/// <c>Proven</c>. Persisted as the smallint <c>classification</c> column (design §5.1: 0/1/2).
/// </summary>
public enum ContentClassification
{
    Undetermined = 0,
    Trending = 1,
    Proven = 2
}

/// <summary>
/// Classifies a single candidate from ONE observation snapshot (CD-06). This is honestly a proxy,
/// not a measurement: real growth VELOCITY needs a second observation over time, which is exactly
/// what the append-only <c>discovered_candidates</c> table (D6) is built to eventually enable —
/// today there is only ever one row to look at when this runs, so "Trending" here means "recent and
/// currently performing above the account's typical rate", not "accelerating".
/// </summary>
public static class TrendingProvenClassifier
{
    /// <summary>Assumed product value (design §0): candidates published within this window are
    /// eligible for Trending.</summary>
    private static readonly TimeSpan TrendingWindow = TimeSpan.FromDays(14);

    /// <summary>Assumed product value (design §0): candidates older than this floor are eligible for
    /// Proven.</summary>
    private static readonly TimeSpan ProvenFloor = TimeSpan.FromDays(90);

    /// <summary>
    /// Not an owner-specified constant (design lists window/floor/N/TTL/K but no explicit Proven
    /// multiplier) — chosen conservatively so "Proven" means meaningfully, not marginally, above
    /// the baseline. Revisit if product feedback disagrees.
    /// </summary>
    private const double ProvenPerformanceFactor = 1.5;

    public static ContentClassification Classify(
        DiscoveredContent candidate,
        IReadOnlyList<DiscoveredContent> set,
        IReadOnlyDictionary<string, AccountBaseline> baselines,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(baselines);

        // CD-06: unavailable publish date wins FIRST and UNCONDITIONALLY — never defaulted to
        // Proven/Trending just because other metrics look strong.
        if (candidate.PublishedAtUtc is null)
        {
            return ContentClassification.Undetermined;
        }

        var age = nowUtc - candidate.PublishedAtUtc.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var referenceMedianViews = ResolveBaselineMedianViews(candidate, baselines) ?? ComputeSetMedianViews(set);

        if (age <= TrendingWindow && candidate.Views is not null && referenceMedianViews is not null)
        {
            var viewsPerDay = candidate.Views.Value / Math.Max(age.TotalDays, 1.0);

            // "MedianViews/day" proxy: the baseline carries one median-per-post figure with no
            // inherent duration, so it is spread over the trending window to get a comparable daily
            // rate. This is the honest, documented approximation the design calls for (§6.3) — not a
            // measured per-day figure.
            var referenceViewsPerDay = referenceMedianViews.Value / TrendingWindow.TotalDays;

            if (viewsPerDay > referenceViewsPerDay)
            {
                return ContentClassification.Trending;
            }
        }

        if (age >= ProvenFloor
            && candidate.Views is not null
            && referenceMedianViews is not null
            && referenceMedianViews.Value > 0
            && candidate.Views.Value > referenceMedianViews.Value * ProvenPerformanceFactor)
        {
            return ContentClassification.Proven;
        }

        return ContentClassification.Undetermined;
    }

    private static double? ResolveBaselineMedianViews(
        DiscoveredContent candidate,
        IReadOnlyDictionary<string, AccountBaseline> baselines)
    {
        var key = AccountBaseline.KeyFor(candidate.NetworkSource, candidate.SourceHandle);

        return baselines.TryGetValue(key, out var baseline) ? baseline.MedianViews : null;
    }

    private static double? ComputeSetMedianViews(IReadOnlyList<DiscoveredContent> set)
    {
        var views = set
            .Where(c => c.Views is not null)
            .Select(c => (double)c.Views!.Value)
            .OrderBy(v => v)
            .ToList();

        if (views.Count == 0)
        {
            return null;
        }

        var n = views.Count;

        return n % 2 == 1 ? views[n / 2] : (views[n / 2 - 1] + views[n / 2]) / 2.0;
    }
}
