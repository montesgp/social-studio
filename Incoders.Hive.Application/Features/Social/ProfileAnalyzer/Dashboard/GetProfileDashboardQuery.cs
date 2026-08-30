using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentPlans;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Dashboard;

/// <summary>
/// Returns the full dashboard projection for a completed ProfileAnalysis (PA-16, PA-17, PA-18).
/// No re-scraping: all data comes from persisted JSON columns.
/// When the analysis has a linked client, a comparison query is also hydrated (PA-17).
/// Graceful degradation: null optional fields are returned as null, never errored (PA-18).
/// </summary>
public sealed record GetProfileDashboardQuery(
    Guid AnalysisId,
    Guid OrganizationId) : IRequest<Result<ProfileDashboardResponse>>;

/// <summary>
/// Handler for <see cref="GetProfileDashboardQuery"/>.
/// Reads the persisted analysis, deserializes the server-derived stats + AI analysis,
/// projects the dashboard DTO. For client-linked analyses, loads the current-period
/// ContentPlan and GeneralStrategy to produce a comparison panel (PA-17).
/// </summary>
public sealed class GetProfileDashboardQueryHandler
    : IRequestHandler<GetProfileDashboardQuery, Result<ProfileDashboardResponse>>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IProfileAnalysisRepository _repository;
    private readonly IContentPlanRepository _contentPlanRepo;
    private readonly IGeneralStrategyRepository _generalStrategyRepo;

    public GetProfileDashboardQueryHandler(
        IProfileAnalysisRepository repository,
        IContentPlanRepository contentPlanRepo,
        IGeneralStrategyRepository generalStrategyRepo)
    {
        _repository = repository;
        _contentPlanRepo = contentPlanRepo;
        _generalStrategyRepo = generalStrategyRepo;
    }

    public async Task<Result<ProfileDashboardResponse>> Handle(
        GetProfileDashboardQuery request,
        CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetByIdAsync(
            request.AnalysisId,
            request.OrganizationId,
            cancellationToken);

        if (analysis is null)
        {
            return Result.Failure<ProfileDashboardResponse>(ProfileAnalysisErrors.NotFound);
        }

        // ── Project server-derived stats (PA-16) ─────────────────────────────
        ProfileStatsDto? statsDto = null;
        if (analysis.ServerDerivedStatsJson is not null)
        {
            var stats = DeserializeStats(analysis.ServerDerivedStatsJson);
            if (stats is not null)
            {
                statsDto = new ProfileStatsDto(
                    stats.EngagementRate,
                    stats.MedianViews,
                    stats.AvgLikesByWeekday.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new WeekdayMetricDto(kvp.Value.AvgLikes, kvp.Value.AvgComments, kvp.Value.AvgViews)),
                    stats.AvgCommentsByWeekday.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new WeekdayMetricDto(kvp.Value.AvgLikes, kvp.Value.AvgComments, kvp.Value.AvgViews)),
                    stats.AvgViewsByWeekday.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new WeekdayMetricDto(kvp.Value.AvgLikes, kvp.Value.AvgComments, kvp.Value.AvgViews)),
                    stats.ContentTypeMix,
                    stats.PostingCadenceDays,
                    stats.OwnerPostCount,
                    stats.DegradedPostCount);
            }
        }

        // ── Project raw owner posts for client-side dynamism (PA-16) ─────────
        var ownerPosts = DeserializeOwnerPosts(analysis.RawPostsDatasetJson);

        // ── Project AI analysis (PA-16, PA-18) ───────────────────────────────
        ProfileAiAnalysisDto? aiAnalysisDto = null;
        if (analysis.AiAnalysisJson is not null)
        {
            aiAnalysisDto = DeserializeAiAnalysis(analysis.AiAnalysisJson);
        }

        // ── Comparison query — only when client is linked (PA-17) ────────────
        ProfileComparisonDto? comparison = null;
        if (analysis.ClientId.HasValue)
        {
            comparison = await BuildComparisonAsync(
                analysis.ClientId.Value,
                analysis.OrganizationId,
                cancellationToken);
        }

        var response = new ProfileDashboardResponse(
            analysis.Id,
            analysis.OrganizationId,
            analysis.ClientId,
            analysis.ProfileUrlOrHandle,
            analysis.Status.ToString(),
            null,   // ProfileSnapshotDto: not separately persisted (profile snapshot stored in AI input only)
            statsDto,
            ownerPosts,
            comparison,
            aiAnalysisDto,
            analysis.CreatedAtUtc,
            analysis.CompletedAtUtc);

        return Result.Success(response);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads the client's current-period ContentPlan and GeneralStrategy.
    /// Returns null when the client has neither (PA-17: omit comparison panel).
    /// </summary>
    private async Task<ProfileComparisonDto?> BuildComparisonAsync(
        Guid clientId,
        Guid organizationId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var periodResult = Period.Monthly(now.Year, now.Month);
        if (periodResult.IsFailure) return null;

        // EF Core's DbContext is NOT thread-safe and both repositories share the same scoped
        // DbContext — these queries MUST run sequentially. Task.WhenAll here caused
        // "A second operation was started on this context instance before a previous operation completed."
        var plan = await _contentPlanRepo.GetByClientAndPeriodAsync(
            organizationId, clientId, periodResult.Value, ct);
        var strategy = await _generalStrategyRepo.GetByClientIdAsync(
            clientId, organizationId, ct);

        // PA-17: omit comparison entirely when no plan data exists
        if (plan is null && strategy is null) return null;

        return new ProfileComparisonDto(
            plan?.Objective.Objectives,
            plan?.Objective.Kpis ?? [],
            plan?.ContentStrategy.StrategyText,
            strategy?.ContentPillars ?? [],
            strategy?.TargetAudience,
            strategy?.PostingFrequency);
    }

    private ProfileAiAnalysisDto? DeserializeAiAnalysis(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<RawAiAnalysisJson>(json, JsonOpts);
            if (raw is null) return null;
            return new ProfileAiAnalysisDto(
                raw.Summary,
                raw.AudienceProfile,
                raw.ContentPillars ?? [],
                raw.TopContentTypes ?? [],
                raw.PostingDays ?? [],
                (raw.DetectedBrandColors ?? [])
                    .Select(c => new DetectedBrandColorDto(c.Name ?? string.Empty, c.Hex ?? string.Empty))
                    .ToList(),
                raw.DetectedFonts ?? [],
                raw.DetectedBackgroundImages ?? [],
                raw.VoiceTone,
                raw.RecommendedKpis ?? [],
                raw.MonthlyObjectiveSuggestion,
                raw.ContentStrategySuggestion,
                raw.Strengths ?? [],
                raw.Weaknesses ?? [],
                raw.Opportunities ?? [],
                raw.Confidence,
                raw.Warnings ?? []);
        }
        catch
        {
            return null;
        }
    }

    private ProfileStats? DeserializeStats(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ProfileStats>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<ProfilePostDto> DeserializeOwnerPosts(string? json)
    {
        if (json is null) return [];
        try
        {
            var raw = JsonSerializer.Deserialize<List<RawPostJson>>(json, JsonOpts);
            if (raw is null) return [];
            return raw.Select(p => new ProfilePostDto(
                p.PostId ?? string.Empty,
                p.MediaType ?? string.Empty,
                p.LikesCount,
                p.CommentsCount,
                p.VideoViewCount,
                DateTimeOffset.TryParse(p.Timestamp, out var ts) ? ts : DateTimeOffset.UtcNow,
                p.IsPaidPartnership,
                p.Hashtags ?? [])).ToList();
        }
        catch
        {
            return [];
        }
    }

    // Internal record for deserializing raw posts JSON.
    private sealed record RawPostJson(
        string? PostId,
        string? MediaType,
        long LikesCount,
        long CommentsCount,
        long? VideoViewCount,
        string? Timestamp,
        bool IsPaidPartnership,
        IReadOnlyList<string>? Hashtags);

    // Internal records for deserializing AI analysis JSON (stored as ProfileAnalysisResult shape).
    private sealed record RawAiAnalysisJson(
        string? Summary,
        string? AudienceProfile,
        IReadOnlyList<string>? ContentPillars,
        IReadOnlyList<string>? TopContentTypes,
        IReadOnlyList<string>? PostingDays,
        IReadOnlyList<RawColorJson>? DetectedBrandColors,
        IReadOnlyList<string>? DetectedFonts,
        IReadOnlyList<string>? DetectedBackgroundImages,
        string? VoiceTone,
        IReadOnlyList<string>? RecommendedKpis,
        string? MonthlyObjectiveSuggestion,
        string? ContentStrategySuggestion,
        IReadOnlyList<string>? Strengths,
        IReadOnlyList<string>? Weaknesses,
        IReadOnlyList<string>? Opportunities,
        double Confidence,
        IReadOnlyList<string>? Warnings);

    private sealed record RawColorJson(string? Name, string? Hex);
}
