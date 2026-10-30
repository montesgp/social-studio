using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Tests.Domain.Social.ProfileAnalysis;

/// <summary>
/// TDD tests for WU-1 task 1.3/1.4: ProfileStats.AverageSlidesPerCarousel (CAG-PA-02).
/// </summary>
public sealed class ProfileStatsAverageSlidesTests
{
    // -----------------------------------------------------------------------
    // Field existence
    // -----------------------------------------------------------------------

    [Fact]
    public void ProfileStats_ExposesAverageSlidesPerCarousel_AsNullableDouble()
    {
        var stats = CreateStats(averageSlidesPerCarousel: null);

        double? avg = stats.AverageSlidesPerCarousel;

        Assert.Null(avg);
    }

    [Fact]
    public void ProfileStats_ConstructionSucceeds_WithNullAverageSlidesPerCarousel()
    {
        var stats = CreateStats(averageSlidesPerCarousel: null);

        Assert.NotNull(stats);
        Assert.Null(stats.AverageSlidesPerCarousel);
    }

    [Fact]
    public void ProfileStats_ConstructionSucceeds_WithValuedAverageSlidesPerCarousel()
    {
        var stats = CreateStats(averageSlidesPerCarousel: 5.3);

        Assert.Equal(5.3, stats.AverageSlidesPerCarousel);
    }

    [Fact]
    public void ProfileStats_AverageSlidesPerCarousel_StoredUnclamped()
    {
        // The raw value is stored as-is; clamping only happens in the prompt hint (design §6).
        var stats = CreateStats(averageSlidesPerCarousel: 12.7);

        Assert.Equal(12.7, stats.AverageSlidesPerCarousel);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ProfileStats CreateStats(double? averageSlidesPerCarousel) =>
        new ProfileStats(
            EngagementRate: 3.5,
            MedianViews: null,
            AvgLikesByWeekday: new Dictionary<int, WeekdayMetric>(),
            AvgCommentsByWeekday: new Dictionary<int, WeekdayMetric>(),
            AvgViewsByWeekday: new Dictionary<int, WeekdayMetric>(),
            ContentTypeMix: new Dictionary<string, double>(),
            PostingCadenceDays: 2.0,
            OwnerPostCount: 10,
            DegradedPostCount: 0,
            AverageSlidesPerCarousel: averageSlidesPerCarousel);
}
