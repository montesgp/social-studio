using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// Orchestrates the full profile analysis pipeline for a queued job.
/// Pipeline stages:
/// 1. Load record. Set status → ScrapingProfile.
/// 2. IProfileScrapeResolver.ResolveAsync → profile + posts + diagnostics.
/// 3. IProfileMetricCalculator.Compute → server-derived stats.
/// 4. Set status → ProfileScraped|ProfileScrapedPartial. Persist raw posts + stats.
/// 5. Select AI provider via IDefaultAiSelector.
/// 6. Set status → Analyzing. Call IAiCompletionGateway.CompleteJsonAsync&lt;ProfileAnalysisResult&gt;.
/// 7. Set status → Analyzed. Persist AI analysis JSON + diagnostics.
/// On any failure → Set status → Failed. Persist failure code + diagnostics.
///
/// Mirrors SingleContentAnalyzer structure. Strict hexagonal — no EF/Npgsql/AspNetCore/Supabase types.
/// </summary>
public sealed class ProfileAnalyzerOrchestrator : IProfileAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProfileAnalysisRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IProfileScrapeResolver _scrapeResolver;
    private readonly IProfileMetricCalculator _calculator;
    private readonly IAiCompletionGateway _aiGateway;
    private readonly IDefaultAiSelector _defaultAiSelector;
    private readonly ILogger<ProfileAnalyzerOrchestrator> _logger;

    public ProfileAnalyzerOrchestrator(
        IProfileAnalysisRepository repository,
        IUnitOfWork unitOfWork,
        IClock clock,
        IProfileScrapeResolver scrapeResolver,
        IProfileMetricCalculator calculator,
        IAiCompletionGateway aiGateway,
        IDefaultAiSelector defaultAiSelector,
        ILogger<ProfileAnalyzerOrchestrator>? logger = null)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _scrapeResolver = scrapeResolver;
        _calculator = calculator;
        _aiGateway = aiGateway;
        _defaultAiSelector = defaultAiSelector;
        _logger = logger ?? NullLogger<ProfileAnalyzerOrchestrator>.Instance;
    }

    public async Task RunQueuedAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var analysis = await _repository.GetByIdAsync(analysisId, organizationId, cancellationToken);
        if (analysis is null)
        {
            _logger.LogWarning(
                "Profile Analyzer: queued analysis {AnalysisId} not found for org {OrganizationId}.",
                analysisId,
                organizationId);
            return;
        }

        try
        {
            await RunCoreAsync(analysis, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Profile Analyzer: queued analysis {AnalysisId} threw.", analysisId);
            await PersistFailureAsync(
                analysis,
                "profile_analysis.unexpected_error",
                $"Unexpected error: {ex.Message}",
                [],
                CancellationToken.None);
        }
    }

    private async Task RunCoreAsync(
        ProfileAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var orgId = analysis.OrganizationId;

        // ── Stage 1: ScrapingProfile ───────────────────────────────────────────
        analysis.StartScraping(_clock.UtcNow);
        await PersistAsync(analysis, cancellationToken);

        _logger.LogInformation(
            "Profile Analyzer: starting scrape for analysis {AnalysisId} — {ProfileUrlOrHandle} (org {OrgId}).",
            analysis.Id,
            analysis.ProfileUrlOrHandle,
            orgId);

        // ── Stage 2: Scrape ────────────────────────────────────────────────────
        var scrapeResult = await _scrapeResolver.ResolveAsync(
            new ProfileScrapeRequest(
                orgId,
                analysis.Id,
                analysis.ClientId,
                analysis.ProfileUrlOrHandle,
                "instagram",
                "profile"),
            cancellationToken);

        if (scrapeResult.IsFailure)
        {
            _logger.LogWarning(
                "Profile Analyzer: scrape failed for analysis {AnalysisId}: {ErrorCode}.",
                analysis.Id,
                scrapeResult.Error.Code);
            await PersistFailureAsync(analysis, scrapeResult.Error.Code, null, [], cancellationToken);
            return;
        }

        // ── Stage 3: Compute stats ─────────────────────────────────────────────
        var scrape = scrapeResult.Value;
        var statsResult = _calculator.Compute(
            [.. scrape.OwnerPosts, .. scrape.Reels],
            scrape.Profile);

        if (statsResult.IsFailure)
        {
            _logger.LogWarning(
                "Profile Analyzer: metric computation failed for analysis {AnalysisId}: {ErrorCode}.",
                analysis.Id,
                statsResult.Error.Code);
            await PersistFailureAsync(analysis, statsResult.Error.Code, null, scrape.Diagnostics, cancellationToken);
            return;
        }

        // ── Stage 4: ProfileScraped|ProfileScrapedPartial ──────────────────────
        analysis.CompleteScrape(scrape.IsPartial, _clock.UtcNow);
        analysis.SetRawData(
            rawPostsJson: JsonSerializer.Serialize(scrape.OwnerPosts, JsonOptions),
            statsJson: JsonSerializer.Serialize(statsResult.Value, JsonOptions),
            diagnosticsJson: JsonSerializer.Serialize(scrape.Diagnostics, JsonOptions));
        await PersistAsync(analysis, cancellationToken);

        _logger.LogInformation(
            "Profile Analyzer: scrape completed for analysis {AnalysisId} (partial={IsPartial}, ownerPosts={OwnerPostCount}).",
            analysis.Id,
            scrape.IsPartial,
            statsResult.Value.OwnerPostCount);

        // ── Stage 5: AI provider selection ────────────────────────────────────
        var aiSelection = await _defaultAiSelector.SelectAsync(orgId, cancellationToken);
        if (aiSelection.IsFailure)
        {
            _logger.LogWarning(
                "Profile Analyzer: AI provider selection failed for org {OrgId}: {ErrorCode}.",
                orgId,
                aiSelection.Error.Code);
            await PersistFailureAsync(analysis, aiSelection.Error.Code, null, scrape.Diagnostics, cancellationToken);
            return;
        }

        // ── Stage 6: Analyzing ─────────────────────────────────────────────────
        analysis.StartAnalyzing(_clock.UtcNow);
        await PersistAsync(analysis, cancellationToken);

        _logger.LogInformation(
            "Profile Analyzer: running AI analysis {AnalysisId} using provider {ProviderKey} (model {Model}).",
            analysis.Id,
            aiSelection.Value.ProviderKey,
            aiSelection.Value.Model ?? "(provider default)");

        var aiInput = CreateAiInput(scrape.Profile, statsResult.Value, scrape.OwnerPosts);

        var aiResult = await _aiGateway.CompleteJsonAsync<ProfileAnalysisResult>(
            new AiJsonCompletionRequest(
                orgId,
                analysis.Id,
                "ai-analysis",
                ProfileAnalyzerPromptVersions.AnalysisV1,
                JsonSerializer.Serialize(aiInput, JsonOptions),
                aiSelection.Value.ProviderKey,
                aiSelection.Value.Model),
            cancellationToken);

        if (aiResult.IsFailure)
        {
            _logger.LogWarning(
                "Profile Analyzer: AI analysis failed for {AnalysisId} (provider {ProviderKey}): {ErrorCode}.",
                analysis.Id,
                aiSelection.Value.ProviderKey,
                aiResult.Error.Code);
            await PersistFailureAsync(analysis, aiResult.Error.Code, null, scrape.Diagnostics, cancellationToken);
            return;
        }

        // ── Stage 7: Analyzed ──────────────────────────────────────────────────
        var aiAnalysis = MapAiAnalysis(aiResult.Value.Payload);
        var completedAt = _clock.UtcNow;
        analysis.CompleteAnalysis(aiAnalysis, completedAt);
        analysis.SetAiAnalysis(
            aiAnalysisJson: JsonSerializer.Serialize(aiResult.Value.Payload, JsonOptions),
            diagnosticsJson: JsonSerializer.Serialize(scrape.Diagnostics, JsonOptions));
        await PersistAsync(analysis, cancellationToken);

        _logger.LogInformation(
            "Profile Analyzer: analysis {AnalysisId} completed successfully (provider {ProviderKey}).",
            analysis.Id,
            aiSelection.Value.ProviderKey);
    }

    private async Task PersistAsync(
        ProfileAnalysis analysis,
        CancellationToken cancellationToken)
    {
        await _repository.UpdateAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistFailureAsync(
        ProfileAnalysis analysis,
        string failureCode,
        string? message,
        IReadOnlyList<Domain.Integrations.ProviderFailureDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        analysis.MarkFailed(failureCode, message, _clock.UtcNow);
        analysis.SetDiagnostics(JsonSerializer.Serialize(diagnostics, JsonOptions));
        await _repository.UpdateAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static ProfileAiAnalysisInput CreateAiInput(
        ProfileSnapshot profile,
        ProfileStats stats,
        IReadOnlyList<Domain.Social.ProfileAnalysis.ProfilePost> ownerPosts) =>
        new(
            "Analyze this Instagram profile and respond with ONLY the raw JSON object matching the expected schema. " +
            "Do not wrap it in markdown code fences and do not add any text before or after the JSON.",
            profile,
            stats,
            ownerPosts.Take(50).ToList(), // limit to avoid oversized prompts
            new ProfileAiExpectedOutput(
                "summary, audienceProfile, contentPillars, topContentTypes, postingDays, " +
                "detectedBrandColors, detectedFonts, detectedBackgroundImages, voiceTone, " +
                "recommendedKpis, monthlyObjectiveSuggestion, contentStrategySuggestion, " +
                "strengths, weaknesses, opportunities, confidence, warnings",
                "confidence is a number from 0 to 1",
                "detectedBrandColors items have name and hex fields"));

    private static ProfileAiAnalysis MapAiAnalysis(ProfileAnalysisResult result) =>
        new(
            Summary: result.Summary,
            AudienceProfile: result.AudienceProfile,
            ContentPillars: result.ContentPillars,
            TopContentTypes: result.TopContentTypes,
            PostingDays: result.PostingDays,
            DetectedBrandColors: result.DetectedBrandColors
                .Select(c => new DetectedBrandColor(c.Name, c.Hex))
                .ToList(),
            DetectedFonts: result.DetectedFonts,
            DetectedBackgroundImages: result.DetectedBackgroundImages,
            VoiceTone: result.VoiceTone,
            RecommendedKpis: result.RecommendedKpis,
            MonthlyObjectiveSuggestion: result.MonthlyObjectiveSuggestion,
            ContentStrategySuggestion: result.ContentStrategySuggestion,
            Strengths: result.Strengths,
            Weaknesses: result.Weaknesses,
            Opportunities: result.Opportunities,
            Confidence: result.Confidence,
            Warnings: result.Warnings);

    private sealed record ProfileAiAnalysisInput(
        string Instruction,
        ProfileSnapshot Profile,
        ProfileStats Stats,
        IReadOnlyList<Domain.Social.ProfileAnalysis.ProfilePost> SamplePosts,
        ProfileAiExpectedOutput ExpectedOutput);

    private sealed record ProfileAiExpectedOutput(
        string RequiredFields,
        string ConfidenceNote,
        string BrandColorNote);
}
