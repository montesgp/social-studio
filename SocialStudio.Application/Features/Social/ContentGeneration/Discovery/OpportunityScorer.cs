using SocialStudio.Application.Abstractions.Social.ContentDiscovery;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Discovery;

/// <summary>One candidate paired with its computed opportunity score, ranked highest first.</summary>
public sealed record ScoredCandidate(DiscoveredContent Candidate, double Score);

/// <summary>
/// Pure opportunity scorer (CD-04, CD-05, design §6.1-§6.2). No I/O, no EF, no provider types — only
/// plain records and a caller-supplied <c>nowUtc</c>. That single choice (never <c>DateTime.UtcNow</c>
/// read internally) is what makes every age-dependent signal deterministic and unit-testable.
///
/// <c>score = Σ(wᵢ·sᵢ over AVAILABLE i) / Σ(wᵢ over AVAILABLE i)</c> — a signal the network does not
/// report (e.g. Instagram shares) is EXCLUDED from both numerator and denominator, never treated as
/// zero (CD-04). Each raw signal is min-max normalized WITHIN the candidate set; a degenerate set
/// (all equal, or a single candidate) yields <c>0.5</c> for that signal rather than a divide-by-zero.
/// </summary>
public static class OpportunityScorer
{
    private const double AbsoluteEngagementWeight = 0.25;
    private const double EngagementRateWeight = 0.20;
    private const double ViewsPerDayWeight = 0.20;
    private const double RecencyWeight = 0.10;
    private const double RelativePerformanceWeight = 0.15;
    private const double CoOccurrenceWeight = 0.10;

    /// <summary>Same window used for the "recency" signal's linear decay as the classifier's proven
    /// floor (design §6.1) — recency counts down to 0 by the time a candidate stops being provably
    /// fresh.</summary>
    private static readonly TimeSpan RecencyDecayWindow = TimeSpan.FromDays(90);

    public static IReadOnlyList<ScoredCandidate> Score(
        IReadOnlyList<DiscoveredContent> set,
        IReadOnlyDictionary<string, AccountBaseline> baselines,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(baselines);

        if (set.Count == 0)
        {
            return [];
        }

        var absoluteEngagement = NormalizeWithinSet(set.Select(AbsoluteEngagementRaw).ToList());
        var engagementRate = NormalizeWithinSet(set.Select(EngagementRateRaw).ToList());
        var viewsPerDay = NormalizeWithinSet(set.Select(c => ViewsPerDayRaw(c, nowUtc)).ToList());
        var recency = NormalizeWithinSet(set.Select(c => RecencyRaw(c, nowUtc)).ToList());
        var relativePerformance = NormalizeWithinSet(set.Select(c => RelativePerformanceRaw(c, baselines)).ToList());

        // Co-occurrence is already a normalized [0,1] value by construction (design §6.4) and is
        // ALWAYS available — it never drops out of the denominator.
        var coOccurrence = CoOccurrenceSignal.Compute(set);

        var results = new List<ScoredCandidate>(set.Count);

        for (var i = 0; i < set.Count; i++)
        {
            double numerator = 0;
            double denominator = 0;

            Accumulate(ref numerator, ref denominator, AbsoluteEngagementWeight, absoluteEngagement[i]);
            Accumulate(ref numerator, ref denominator, EngagementRateWeight, engagementRate[i]);
            Accumulate(ref numerator, ref denominator, ViewsPerDayWeight, viewsPerDay[i]);
            Accumulate(ref numerator, ref denominator, RecencyWeight, recency[i]);
            Accumulate(ref numerator, ref denominator, RelativePerformanceWeight, relativePerformance[i]);
            Accumulate(ref numerator, ref denominator, CoOccurrenceWeight, coOccurrence[i]);

            // Every candidate always has co-occurrence available, so denominator is never 0 here —
            // but guard defensively rather than assume it.
            var score = denominator > 0 ? numerator / denominator : 0.5;

            results.Add(new ScoredCandidate(set[i], score));
        }

        // Total order: highest score first, ties broken on the same dedup identity used everywhere
        // else (design §6.2) so ranking never depends on input order.
        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => CandidateIdentity.From(r.Candidate.NetworkSource, r.Candidate.ContentUrl), StringComparer.Ordinal)
            .ToList();
    }

    private static void Accumulate(ref double numerator, ref double denominator, double weight, double? normalizedValue)
    {
        if (normalizedValue is null)
        {
            return;
        }

        numerator += weight * normalizedValue.Value;
        denominator += weight;
    }

    // ── Raw signal extraction (design §6.1) ─────────────────────────────────────

    private static double? AbsoluteEngagementRaw(DiscoveredContent c)
    {
        if (c.Likes is null && c.Comments is null && c.Shares is null && c.Saves is null)
        {
            return null;
        }

        return (c.Likes ?? 0) + (c.Comments ?? 0) + (c.Shares ?? 0) + (c.Saves ?? 0);
    }

    private static double? EngagementRateRaw(DiscoveredContent c)
    {
        if (c.Views is null || c.Views.Value <= 0)
        {
            return null;
        }

        var engagement = (c.Likes ?? 0) + (c.Comments ?? 0) + (c.Shares ?? 0) + (c.Saves ?? 0);

        return engagement / (double)c.Views.Value;
    }

    private static double? ViewsPerDayRaw(DiscoveredContent c, DateTimeOffset nowUtc)
    {
        if (c.Views is null || c.PublishedAtUtc is null)
        {
            return null;
        }

        var ageDays = Math.Max((nowUtc - c.PublishedAtUtc.Value).TotalDays, 1.0);

        return c.Views.Value / ageDays;
    }

    private static double? RecencyRaw(DiscoveredContent c, DateTimeOffset nowUtc)
    {
        if (c.PublishedAtUtc is null)
        {
            return null;
        }

        var ageDays = Math.Max((nowUtc - c.PublishedAtUtc.Value).TotalDays, 0.0);

        return Math.Max(0.0, 1.0 - (ageDays / RecencyDecayWindow.TotalDays));
    }

    private static double? RelativePerformanceRaw(DiscoveredContent c, IReadOnlyDictionary<string, AccountBaseline> baselines)
    {
        if (c.Views is null)
        {
            return null;
        }

        var key = AccountBaseline.KeyFor(c.NetworkSource, c.SourceHandle);

        if (!baselines.TryGetValue(key, out var baseline) || baseline.MedianViews is null || baseline.MedianViews.Value <= 0)
        {
            // D-CD-07: no fabricated baseline. The signal is simply unavailable for this candidate —
            // it drops out of the denominator, it is never defaulted to a fixed number.
            return null;
        }

        return c.Views.Value / baseline.MedianViews.Value;
    }

    // ── Set-relative normalization (design §6.1-§6.2) ───────────────────────────

    private static IReadOnlyList<double?> NormalizeWithinSet(IReadOnlyList<double?> raw)
    {
        var available = raw.Where(v => v.HasValue).Select(v => v!.Value).ToList();

        if (available.Count == 0)
        {
            // Nobody in the set reports this signal — stays unavailable for everyone.
            return raw;
        }

        var min = available.Min();
        var max = available.Max();

        if (available.Count == 1 || Math.Abs(max - min) < double.Epsilon)
        {
            // Degenerate set: a single candidate, or every available value identical.
            return raw.Select(v => v.HasValue ? (double?)0.5 : null).ToList();
        }

        return raw.Select(v => v.HasValue ? (double?)((v.Value - min) / (max - min)) : null).ToList();
    }
}
