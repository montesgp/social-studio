using Incoders.Hive.Domain.Social.ProfileAnalysis;
using DomainProfileAnalysis = Incoders.Hive.Domain.Social.ProfileAnalysis.ProfileAnalysis;

namespace Incoders.Hive.Tests.Domain.Social.ProfileAnalysis;

public sealed class ProfileAnalysisDomainTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnAccountId = Guid.NewGuid();
    private static readonly DateTime UtcNow = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

    // ── Factory: CreateFromClient ──────────────────────────────────────────

    [Fact]
    public void CreateFromClient_PopulatesFieldsCorrectly_AndStatusIsRequested()
    {
        var analysis = DomainProfileAnalysis.CreateFromClient(
            OrgId, ClientId, OwnAccountId, "@testhandle", UtcNow);

        Assert.Equal(OrgId, analysis.OrganizationId);
        Assert.Equal(ClientId, analysis.ClientId);
        Assert.Equal(OwnAccountId, analysis.OwnAccountId);
        Assert.Equal("@testhandle", analysis.ProfileUrlOrHandle);
        Assert.Equal(ProfileAnalysisStatus.Requested, analysis.Status);
        Assert.Null(analysis.ProfileUrl);
        Assert.NotEqual(Guid.Empty, analysis.Id);
        Assert.Equal(UtcNow, analysis.CreatedAtUtc);
        Assert.Equal(UtcNow, analysis.UpdatedAtUtc);
        Assert.Null(analysis.CompletedAtUtc);
        Assert.Null(analysis.FailureCode);
    }

    [Fact]
    public void CreateFromUrl_PopulatesFieldsCorrectly_ClientAndOwnAccountAreNull()
    {
        var url = "https://www.instagram.com/zachking";

        var analysis = DomainProfileAnalysis.CreateFromUrl(
            OrgId, url, UtcNow);

        Assert.Equal(OrgId, analysis.OrganizationId);
        Assert.Null(analysis.ClientId);
        Assert.Null(analysis.OwnAccountId);
        Assert.Equal(url, analysis.ProfileUrl);
        Assert.Equal(url, analysis.ProfileUrlOrHandle);
        Assert.Equal(ProfileAnalysisStatus.Requested, analysis.Status);
        Assert.NotEqual(Guid.Empty, analysis.Id);
    }

    // ── Happy-path transitions ─────────────────────────────────────────────

    [Fact]
    public void HappyPath_FullTransition_Requested_To_Analyzed()
    {
        var analysis = CreateClientAnalysis();
        var aiAnalysis = BuildAiAnalysis();

        analysis.StartScraping(UtcNow);
        Assert.Equal(ProfileAnalysisStatus.ScrapingProfile, analysis.Status);

        analysis.CompleteScrape(isPartial: false, UtcNow);
        Assert.Equal(ProfileAnalysisStatus.ProfileScraped, analysis.Status);

        analysis.StartAnalyzing(UtcNow);
        Assert.Equal(ProfileAnalysisStatus.Analyzing, analysis.Status);

        analysis.CompleteAnalysis(aiAnalysis, UtcNow);
        Assert.Equal(ProfileAnalysisStatus.Analyzed, analysis.Status);
        Assert.Equal(UtcNow, analysis.CompletedAtUtc);
        Assert.NotNull(analysis.AiAnalysis);
    }

    [Fact]
    public void PartialPath_Requested_To_ProfileScrapedPartial_To_Analyzed()
    {
        var analysis = CreateClientAnalysis();
        var aiAnalysis = BuildAiAnalysis();

        analysis.StartScraping(UtcNow);
        analysis.CompleteScrape(isPartial: true, UtcNow);
        Assert.Equal(ProfileAnalysisStatus.ProfileScrapedPartial, analysis.Status);

        analysis.StartAnalyzing(UtcNow);
        Assert.Equal(ProfileAnalysisStatus.Analyzing, analysis.Status);

        analysis.CompleteAnalysis(aiAnalysis, UtcNow);
        Assert.Equal(ProfileAnalysisStatus.Analyzed, analysis.Status);
    }

    // ── Terminal status invariants ─────────────────────────────────────────

    [Fact]
    public void Failed_IsTerminal_AnyFurtherTransitionThrows()
    {
        var analysis = CreateClientAnalysis();
        analysis.MarkFailed("scrape.error", "provider failed", UtcNow);

        Assert.Equal(ProfileAnalysisStatus.Failed, analysis.Status);

        Assert.Throws<InvalidOperationException>(() => analysis.StartScraping(UtcNow));
        Assert.Throws<InvalidOperationException>(() => analysis.StartAnalyzing(UtcNow));
        Assert.Throws<InvalidOperationException>(() => analysis.CompleteScrape(false, UtcNow));
    }

    [Fact]
    public void Analyzed_IsTerminal_AttemptToRestartScrapingThrows()
    {
        var analysis = CreateClientAnalysis();
        var aiAnalysis = BuildAiAnalysis();

        analysis.StartScraping(UtcNow);
        analysis.CompleteScrape(false, UtcNow);
        analysis.StartAnalyzing(UtcNow);
        analysis.CompleteAnalysis(aiAnalysis, UtcNow);

        Assert.Throws<InvalidOperationException>(() => analysis.StartScraping(UtcNow));
        Assert.Throws<InvalidOperationException>(() => analysis.StartAnalyzing(UtcNow));
        Assert.Throws<InvalidOperationException>(() => analysis.MarkFailed("x", null, UtcNow));
    }

    // ── Invalid direct transitions ─────────────────────────────────────────

    [Fact]
    public void InvalidTransition_Requested_To_Analyzing_Throws()
    {
        var analysis = CreateClientAnalysis();
        Assert.Throws<InvalidOperationException>(() => analysis.StartAnalyzing(UtcNow));
    }

    [Fact]
    public void InvalidTransition_Requested_To_CompleteAnalysis_Throws()
    {
        var analysis = CreateClientAnalysis();
        Assert.Throws<InvalidOperationException>(() =>
            analysis.CompleteAnalysis(BuildAiAnalysis(), UtcNow));
    }

    [Fact]
    public void InvalidTransition_ScrapingProfile_To_CompleteAnalysis_Throws()
    {
        var analysis = CreateClientAnalysis();
        analysis.StartScraping(UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            analysis.CompleteAnalysis(BuildAiAnalysis(), UtcNow));
    }

    // ── MarkFailed records code and message ───────────────────────────────

    [Fact]
    public void MarkFailed_SetsFailureCodeAndUpdatedAt()
    {
        var analysis = CreateClientAnalysis();
        analysis.MarkFailed("Integration.ApifyNotConfigured", "Apify not configured", UtcNow);

        Assert.Equal(ProfileAnalysisStatus.Failed, analysis.Status);
        Assert.Equal("Integration.ApifyNotConfigured", analysis.FailureCode);
        Assert.Equal(UtcNow, analysis.UpdatedAtUtc);
    }

    [Fact]
    public void MarkFailed_WithoutMessage_StillSetsStatus()
    {
        var analysis = CreateClientAnalysis();
        analysis.StartScraping(UtcNow);
        analysis.MarkFailed("NoUsableOwnerPosts", null, UtcNow);

        Assert.Equal(ProfileAnalysisStatus.Failed, analysis.Status);
        Assert.Equal("NoUsableOwnerPosts", analysis.FailureCode);
    }

    // ── Value objects ──────────────────────────────────────────────────────

    [Fact]
    public void ProfilePost_IsOwnerPost_ReturnsTrueWhenOwnerIdMatches()
    {
        var instagramId = "12345";
        var post = new ProfilePost(
            PostId: "post1",
            MediaType: "Image",
            LikesCount: 100,
            CommentsCount: 10,
            VideoViewCount: null,
            Timestamp: DateTimeOffset.UtcNow,
            OwnerId: instagramId,
            OwnerUsername: "testuser",
            IsPaidPartnership: false,
            IsDegraded: false,
            Hashtags: []);

        Assert.True(post.IsOwnerPost(instagramId));
        Assert.False(post.IsOwnerPost("99999"));
    }

    [Fact]
    public void ProfilePost_IsVideo_ReturnsTrueForVideoAndReel()
    {
        var videoPost = new ProfilePost("p1", "Video", 0, 0, 5000, DateTimeOffset.UtcNow,
            "owner1", "user", false, false, []);
        var reelPost = new ProfilePost("p2", "Reel", 0, 0, 3000, DateTimeOffset.UtcNow,
            "owner1", "user", false, false, []);
        var imagePost = new ProfilePost("p3", "Image", 0, 0, null, DateTimeOffset.UtcNow,
            "owner1", "user", false, false, []);
        var carouselPost = new ProfilePost("p4", "Carousel", 0, 0, null, DateTimeOffset.UtcNow,
            "owner1", "user", false, false, []);

        Assert.True(videoPost.IsVideo);
        Assert.True(reelPost.IsVideo);
        Assert.False(imagePost.IsVideo);
        Assert.False(carouselPost.IsVideo);
    }

    [Fact]
    public void ProfileSnapshot_CreatesCorrectly()
    {
        var snapshot = new ProfileSnapshot(
            InstagramId: "12345",
            Username: "testuser",
            FollowersCount: 50000,
            PostsCount: 100,
            ReelsCount: null,
            EngagementRate: 3.5,
            MedianDaysBetweenPosts: null,
            ProfilePicUrl: "https://example.com/pic.jpg",
            Biography: "Test bio",
            IsVerified: false,
            IsBusinessAccount: true,
            BusinessCategoryName: "Marketing");

        Assert.Equal("12345", snapshot.InstagramId);
        Assert.Equal("testuser", snapshot.Username);
        Assert.Equal(50000, snapshot.FollowersCount);
        Assert.True(snapshot.IsBusinessAccount);
    }

    [Fact]
    public void ProfileStats_CreatesCorrectly()
    {
        var weekdayAvgs = new Dictionary<int, WeekdayMetric>
        {
            [1] = new WeekdayMetric(AvgLikes: 150, AvgComments: 12, AvgViews: null)
        };

        var stats = new ProfileStats(
            EngagementRate: 3.5,
            MedianViews: 10000,
            AvgLikesByWeekday: weekdayAvgs,
            AvgCommentsByWeekday: weekdayAvgs,
            AvgViewsByWeekday: weekdayAvgs,
            ContentTypeMix: new Dictionary<string, double> { ["Video"] = 0.6, ["Image"] = 0.4 },
            PostingCadenceDays: 3.2,
            OwnerPostCount: 8,
            DegradedPostCount: 2);

        Assert.Equal(3.5, stats.EngagementRate);
        Assert.Equal(10000, stats.MedianViews);
        Assert.Equal(8, stats.OwnerPostCount);
        Assert.Equal(2, stats.DegradedPostCount);
    }

    [Fact]
    public void ProfileAiAnalysis_CreatesCorrectly()
    {
        var aiAnalysis = BuildAiAnalysis();

        Assert.Equal("This profile has strong engagement.", aiAnalysis.Summary);
        Assert.NotEmpty(aiAnalysis.ContentPillars);
        Assert.Equal(0.85, aiAnalysis.Confidence);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static DomainProfileAnalysis CreateClientAnalysis() =>
        DomainProfileAnalysis.CreateFromClient(
            OrgId, ClientId, OwnAccountId, "@testhandle", UtcNow);

    private static ProfileAiAnalysis BuildAiAnalysis() =>
        new ProfileAiAnalysis(
            Summary: "This profile has strong engagement.",
            AudienceProfile: "Young professionals 25-34.",
            ContentPillars: ["Motivational", "Educational"],
            TopContentTypes: ["Video", "Carousel"],
            PostingDays: ["Mon", "Wed", "Fri"],
            DetectedBrandColors: [],
            DetectedFonts: [],
            DetectedBackgroundImages: [],
            VoiceTone: "Professional and inspiring",
            RecommendedKpis: ["Engagement rate", "Reach"],
            MonthlyObjectiveSuggestion: "Increase followers by 5%.",
            ContentStrategySuggestion: "Focus on short-form video.",
            Strengths: ["High engagement"],
            Weaknesses: ["Low posting frequency"],
            Opportunities: ["Trending topics"],
            Confidence: 0.85,
            Warnings: []);
}
