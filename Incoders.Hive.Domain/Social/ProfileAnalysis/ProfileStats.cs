namespace Incoders.Hive.Domain.Social.ProfileAnalysis;

/// <summary>
/// Server-derived aggregate statistics for a profile analysis run.
/// All values are computed by <c>IProfileMetricCalculator</c> from owner-filtered,
/// non-degraded posts and stored as part of <see cref="ProfileAnalysis.ServerDerivedStatsJson"/>.
/// </summary>
/// <param name="EngagementRate">Account-level engagement rate (likes+comments / followers * 100).</param>
/// <param name="MedianViews">Median video view count from Video/Reel posts; null when no video posts exist (PA-09).</param>
/// <param name="AvgLikesByWeekday">Average likes per weekday bucket (0=Monday, 6=Sunday). UTC-normalised.</param>
/// <param name="AvgCommentsByWeekday">Average comments per weekday bucket. UTC-normalised.</param>
/// <param name="AvgViewsByWeekday">Average views per weekday bucket. UTC-normalised; null views for non-video posts.</param>
/// <param name="ContentTypeMix">Percentage breakdown by media type ("Image", "Video", "Carousel").</param>
/// <param name="PostingCadenceDays">Average days between consecutive owner posts.</param>
/// <param name="OwnerPostCount">Count of non-degraded owner posts used for computations.</param>
/// <param name="DegradedPostCount">Count of owner posts excluded because _degraded == true.</param>
/// <param name="AverageSlidesPerCarousel">Mean number of child slides per Carousel post, rounded
///     to 1 decimal place. Null when no Carousel posts have a non-null CarouselChildrenCount.
///     Stored as the raw computed value; the prompt hint clamps it to 3–10 (CAG-PA-02, design §6).</param>
public sealed record ProfileStats(
    double EngagementRate,
    double? MedianViews,
    IReadOnlyDictionary<int, WeekdayMetric> AvgLikesByWeekday,
    IReadOnlyDictionary<int, WeekdayMetric> AvgCommentsByWeekday,
    IReadOnlyDictionary<int, WeekdayMetric> AvgViewsByWeekday,
    IReadOnlyDictionary<string, double> ContentTypeMix,
    double PostingCadenceDays,
    int OwnerPostCount,
    int DegradedPostCount,
    double? AverageSlidesPerCarousel = null);

/// <summary>
/// Average metric value for a specific weekday bucket.
/// Weekday numbering follows ISO 8601: 0 = Monday, 6 = Sunday.
/// Timestamps are normalised to UTC before weekday assignment (PA-12).
/// </summary>
public sealed record WeekdayMetric(double AvgLikes, double AvgComments, double? AvgViews);
