using System.Text.Json;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;

// ── Request DTOs ───────────────────────────────────────────────────────────────

/// <summary>
/// Request DTO for starting a profile analysis. Exactly one of (ClientId+OwnAccountId) or
/// ProfileUrl must be supplied — not both, not neither (PA-01, PA-02, PA-03).
/// </summary>
public sealed record AnalyzeProfileRequest(
    Guid OrganizationId,
    Guid UserId,
    /// <summary>Client-path: the client id. Null for URL-path analyses.</summary>
    Guid? ClientId,
    /// <summary>Client-path: the own account id. Null for URL-path analyses.</summary>
    Guid? OwnAccountId,
    /// <summary>URL-path: the Instagram profile URL. Null for client-path analyses.</summary>
    string? ProfileUrl);

/// <summary>
/// Target fields that can be imputed from a completed profile analysis (PA-20).
/// Each value maps to a field in BrandManual, ContentPlan, or GeneralStrategy.
/// 11 actionable targets — Logos and Slots are excluded (not AI-derivable; WU-01 spike findings).
/// </summary>
public enum ProfileImputeTarget
{
    // BrandManual targets (4)
    ColorPalette = 1,
    Typography = 2,
    BackgroundTemplates = 3,
    VoiceAndTone = 4,

    // ContentPlan targets (3)
    ObjectiveText = 5,
    ObjectiveKpis = 6,
    ContentStrategyText = 7,

    // GeneralStrategy targets (4)
    ContentPillars = 8,
    TargetAudience = 9,
    PostingFrequency = 10,
    ToneKeywords = 11,
}

/// <summary>
/// Request DTO for the impute endpoint. Carries the selected field targets
/// to apply from a completed profile analysis to the linked client's data (PA-20).
/// </summary>
public sealed record ImputeProfileAnalysisRequest(
    IReadOnlyList<ProfileImputeTarget> Targets);

// ── Response DTOs ──────────────────────────────────────────────────────────────

/// <summary>
/// Status/poll response for a profile analysis. Used by both the start endpoint (202)
/// and the polling endpoint (GET /{id}). The analysis, stats, and posts fields are null
/// until those stages complete. FailureCode is set when the analysis fails (PA-14, PA-15).
/// </summary>
public sealed record AnalyzeProfileStatusResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    Guid? OwnAccountId,
    string ProfileUrlOrHandle,
    string Status,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AnalyzeProfileStatusResponse FromRecord(ProfileAnalysis record) =>
        new(
            record.Id,
            record.OrganizationId,
            record.ClientId,
            record.OwnAccountId,
            record.ProfileUrlOrHandle,
            record.Status.ToString(),
            record.FailureCode,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.CompletedAtUtc);
}

/// <summary>
/// Full dashboard response for a completed profile analysis (PA-16, PA-17, PA-18).
/// All optional fields degrade gracefully — callers must handle null values.
/// </summary>
public sealed record ProfileDashboardResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    string ProfileUrlOrHandle,
    string Status,
    /// <summary>Profile-level snapshot fields.</summary>
    ProfileSnapshotDto? Profile,
    /// <summary>Server-derived aggregate stats.</summary>
    ProfileStatsDto? Stats,
    /// <summary>Raw owner-filtered posts for client-side re-derivation (PA-16).</summary>
    IReadOnlyList<ProfilePostDto> OwnerPosts,
    /// <summary>
    /// Comparison data against the client's active ContentPlan/GeneralStrategy.
    /// Null for URL-path analyses or when no plan data exists (PA-17).
    /// </summary>
    ProfileComparisonDto? Comparison,
    /// <summary>
    /// AI narrative analysis output. Null until the Analyzed stage completes (PA-16, PA-18).
    /// </summary>
    ProfileAiAnalysisDto? AiAnalysis,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

/// <summary>Profile snapshot DTO for dashboard responses.</summary>
public sealed record ProfileSnapshotDto(
    string InstagramId,
    string Username,
    long FollowersCount,
    long PostsCount,
    long? ReelsCount,
    double EngagementRate,
    double? MedianDaysBetweenPosts,
    string? ProfilePicUrl,
    string? Biography,
    bool IsVerified,
    bool IsBusinessAccount,
    string? BusinessCategoryName);

/// <summary>Server-derived stats DTO for dashboard responses.</summary>
public sealed record ProfileStatsDto(
    double EngagementRate,
    double? MedianViews,
    IReadOnlyDictionary<int, WeekdayMetricDto> AvgLikesByWeekday,
    IReadOnlyDictionary<int, WeekdayMetricDto> AvgCommentsByWeekday,
    IReadOnlyDictionary<int, WeekdayMetricDto> AvgViewsByWeekday,
    IReadOnlyDictionary<string, double> ContentTypeMix,
    double PostingCadenceDays,
    int OwnerPostCount,
    int DegradedPostCount);

/// <summary>Weekday metric bucket DTO.</summary>
public sealed record WeekdayMetricDto(
    double AvgLikes,
    double AvgComments,
    double? AvgViews);

/// <summary>Single post DTO for dashboard raw-posts list.</summary>
public sealed record ProfilePostDto(
    string PostId,
    string MediaType,
    long LikesCount,
    long CommentsCount,
    long? VideoViewCount,
    DateTimeOffset Timestamp,
    bool IsPaidPartnership,
    IReadOnlyList<string> Hashtags);

/// <summary>
/// Comparison DTO carrying detected account state versus client's stated objectives.
/// Null when the client has no active ContentPlan or GeneralStrategy (PA-17, PA-18).
/// </summary>
public sealed record ProfileComparisonDto(
    string? ContentPlanObjective,
    IReadOnlyList<string> ContentPlanKpis,
    string? ContentPlanStrategy,
    IReadOnlyList<string> GeneralStrategyPillars,
    string? GeneralStrategyAudience,
    string? GeneralStrategyPostingFrequency);

/// <summary>
/// AI narrative analysis DTO for dashboard responses (PA-16, PA-18).
/// All list fields default to empty to survive partial AI output; callers must handle null strings.
/// </summary>
public sealed record ProfileAiAnalysisDto(
    string? Summary,
    string? AudienceProfile,
    IReadOnlyList<string> ContentPillars,
    IReadOnlyList<string> TopContentTypes,
    IReadOnlyList<string> PostingDays,
    IReadOnlyList<DetectedBrandColorDto> DetectedBrandColors,
    IReadOnlyList<string> DetectedFonts,
    IReadOnlyList<string> DetectedBackgroundImages,
    string? VoiceTone,
    IReadOnlyList<string> RecommendedKpis,
    string? MonthlyObjectiveSuggestion,
    string? ContentStrategySuggestion,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Opportunities,
    double Confidence,
    IReadOnlyList<string> Warnings);

/// <summary>A detected brand colour in the AI analysis output.</summary>
public sealed record DetectedBrandColorDto(string Name, string Hex);

/// <summary>Paged history response for the list endpoint (PA-15).</summary>
public sealed record ProfileAnalysisHistoryResponse(
    IReadOnlyList<ProfileAnalysisHistoryItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>Single item in the history list.</summary>
public sealed record ProfileAnalysisHistoryItemDto(
    Guid Id,
    Guid? ClientId,
    string ProfileUrlOrHandle,
    string Status,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);
