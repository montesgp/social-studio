using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// Server-side metric computation from owner-filtered post data.
/// Pure — no I/O, no external dependencies, no EF types.
///
/// Pipeline:
/// 1. Owner-filter: exclude posts where OwnerId != profile.InstagramId (PA-07).
/// 2. Degrade-filter: exclude posts where IsDegraded == true (PA-08).
/// 3. Guard: return NoUsableOwnerPosts when no usable posts remain (PA-08).
/// 4. Median views: from Video/Reel posts only; null when none (PA-09).
///    Carousel posts excluded even if videoViewCount is accidentally non-null.
/// 5. Weekday buckets: UTC-normalised; ISO-8601 weekday (Mon=0, Sun=6) (PA-12).
/// 6. Content-type mix percentages.
/// 7. Posting cadence: average days between consecutive owner posts.
/// </summary>
public sealed class ProfileMetricCalculator : IProfileMetricCalculator
{
    /// <summary>
    /// videoPlayCount is always null in instagram-profile-scraper mode (WU-01 spike finding, obs #513).
    /// Median views uses videoViewCount exclusively.
    /// </summary>
    public Result<ProfileStats> Compute(
        IReadOnlyList<ProfilePost> allPosts,
        ProfileSnapshot profile)
    {
        // ── Step 1 + 2: Owner-filter + degrade-filter ──────────────────────────
        var ownerPosts = allPosts
            .Where(p => p.IsOwnerPost(profile.InstagramId))
            .ToList();

        var degradedCount = ownerPosts.Count(p => p.IsDegraded);
        var usablePosts = ownerPosts.Where(p => !p.IsDegraded).ToList();

        // ── Step 3: Guard ──────────────────────────────────────────────────────
        if (usablePosts.Count == 0)
        {
            return Result.Failure<ProfileStats>(ProfileAnalysisErrors.NoUsableOwnerPosts);
        }

        // ── Step 4: Median views (Video/Reel only) ─────────────────────────────
        var videoPosts = usablePosts.Where(p => p.IsVideo).ToList();
        double? medianViews = ComputeMedianViews(videoPosts);

        // ── Step 5: Weekday averages (UTC-normalised, ISO-8601) ───────────────
        var likesByWeekday = ComputeWeekdayAverages(usablePosts,
            post => (double)post.LikesCount,
            post => (double)post.CommentsCount,
            post => post.IsVideo ? (double?)post.VideoViewCount : null);

        // ── Step 6: Content-type mix ───────────────────────────────────────────
        var contentTypeMix = ComputeContentTypeMix(usablePosts);

        // ── Step 7: Posting cadence ────────────────────────────────────────────
        var cadence = ComputePostingCadence(usablePosts);

        // Account-level engagement rate: (likes + comments) / followers * 100
        var engagementRate = usablePosts.Count > 0 && profile.FollowersCount > 0
            ? (usablePosts.Average(p => (double)(p.LikesCount + p.CommentsCount)) / profile.FollowersCount) * 100.0
            : 0.0;

        return Result.Success(new ProfileStats(
            EngagementRate: engagementRate,
            MedianViews: medianViews,
            AvgLikesByWeekday: likesByWeekday.avgLikes,
            AvgCommentsByWeekday: likesByWeekday.avgComments,
            AvgViewsByWeekday: likesByWeekday.avgViews,
            ContentTypeMix: contentTypeMix,
            PostingCadenceDays: cadence,
            OwnerPostCount: usablePosts.Count,
            DegradedPostCount: degradedCount));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Computes the median view count from Video/Reel posts.
    /// Carousel posts are excluded from the computation regardless of VideoViewCount value (PA-09).
    /// Returns null when no Video/Reel posts exist.
    /// </summary>
    private static double? ComputeMedianViews(IReadOnlyList<ProfilePost> videoPosts)
    {
        if (videoPosts.Count == 0)
            return null;

        var sorted = videoPosts
            .Select(p => (double)(p.VideoViewCount ?? 0))
            .OrderBy(v => v)
            .ToList();

        var n = sorted.Count;
        if (n % 2 == 1)
            return sorted[n / 2];

        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    /// <summary>
    /// Computes average likes, comments, and views grouped by ISO-8601 weekday (Mon=0, Sun=6).
    /// All timestamps are normalised to UTC before weekday bucket assignment (PA-12).
    /// </summary>
    private static (
        IReadOnlyDictionary<int, WeekdayMetric> avgLikes,
        IReadOnlyDictionary<int, WeekdayMetric> avgComments,
        IReadOnlyDictionary<int, WeekdayMetric> avgViews)
    ComputeWeekdayAverages(
        IReadOnlyList<ProfilePost> posts,
        Func<ProfilePost, double> likesSelector,
        Func<ProfilePost, double> commentsSelector,
        Func<ProfilePost, double?> viewsSelector)
    {
        // Group by ISO-8601 weekday (DayOfWeek.Monday=1 → bucket 0; Sunday=0 → bucket 6)
        var groups = posts.GroupBy(p =>
        {
            var utcDow = p.Timestamp.ToUniversalTime().DayOfWeek;
            // Convert DayOfWeek to ISO-8601 (Monday=0 ... Sunday=6)
            return utcDow == DayOfWeek.Sunday ? 6 : (int)utcDow - 1;
        }).ToDictionary(g => g.Key, g => g.ToList());

        var avgLikes = new Dictionary<int, WeekdayMetric>();
        var avgComments = new Dictionary<int, WeekdayMetric>();
        var avgViews = new Dictionary<int, WeekdayMetric>();

        foreach (var (weekday, weekdayPosts) in groups)
        {
            var avgL = weekdayPosts.Average(likesSelector);
            var avgC = weekdayPosts.Average(commentsSelector);
            var viewValues = weekdayPosts
                .Select(viewsSelector)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            double? avgV = viewValues.Count > 0 ? viewValues.Average() : null;

            var metric = new WeekdayMetric(avgL, avgC, avgV);
            avgLikes[weekday] = metric;
            avgComments[weekday] = metric;
            avgViews[weekday] = metric;
        }

        return (avgLikes, avgComments, avgViews);
    }

    /// <summary>
    /// Computes the percentage breakdown of posts by MediaType.
    /// Uses the usable (non-degraded, owner-filtered) post list.
    /// Percentages sum to exactly 100.
    /// </summary>
    private static IReadOnlyDictionary<string, double> ComputeContentTypeMix(
        IReadOnlyList<ProfilePost> posts)
    {
        if (posts.Count == 0)
            return new Dictionary<string, double>();

        var groups = posts
            .GroupBy(p => p.MediaType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (double)g.Count() / posts.Count * 100.0,
                StringComparer.OrdinalIgnoreCase);

        return groups;
    }

    /// <summary>
    /// Computes the average days between consecutive owner posts.
    /// Posts are sorted descending by timestamp (newest first) then gaps are computed.
    /// Returns 0 when fewer than 2 posts exist.
    /// </summary>
    private static double ComputePostingCadence(IReadOnlyList<ProfilePost> posts)
    {
        if (posts.Count < 2)
            return 0.0;

        var sorted = posts.Select(p => p.Timestamp.ToUniversalTime())
            .OrderBy(t => t)
            .ToList();

        var gaps = new List<double>();
        for (var i = 1; i < sorted.Count; i++)
        {
            gaps.Add((sorted[i] - sorted[i - 1]).TotalDays);
        }

        return gaps.Average();
    }
}
