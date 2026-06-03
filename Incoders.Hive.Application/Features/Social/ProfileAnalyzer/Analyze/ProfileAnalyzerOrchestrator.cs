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
            _logger.LogError(
                ex,
                "Profile Analyzer: unexpected exception " +
                "analysisId={AnalysisId} organizationId={OrganizationId} stage=UnexpectedError.",
                analysisId,
                organizationId);
            await PersistFailureWithDiagnosticsAsync(
                analysis,
                "profile_analysis.unexpected_error",
                $"Unexpected error: {ex.Message}",
                BuildFailureDiagnostics(
                    actorId: string.Empty,
                    fallbackUsed: false,
                    postsReceived: 0,
                    postsProcessed: 0,
                    degradedPostCount: 0,
                    aiProviderKey: null,
                    aiModel: null,
                    failureReasons: ["profile_analysis.unexpected_error"]),
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
            "Profile Analyzer: starting scrape " +
            "analysisId={AnalysisId} organizationId={OrgId} stage=ScrapingProfile handle={ProfileUrlOrHandle}.",
            analysis.Id,
            orgId,
            analysis.ProfileUrlOrHandle);

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
                "Profile Analyzer: scrape failed " +
                "analysisId={AnalysisId} organizationId={OrgId} stage=ScrapeFailed errorCode={ErrorCode}.",
                analysis.Id,
                orgId,
                scrapeResult.Error.Code);

            await PersistFailureWithDiagnosticsAsync(
                analysis,
                scrapeResult.Error.Code,
                null,
                BuildFailureDiagnostics(
                    actorId: string.Empty,
                    fallbackUsed: false,
                    postsReceived: 0,
                    postsProcessed: 0,
                    degradedPostCount: 0,
                    aiProviderKey: null,
                    aiModel: null,
                    failureReasons: [scrapeResult.Error.Code]),
                cancellationToken);
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
                "Profile Analyzer: metric computation failed " +
                "analysisId={AnalysisId} organizationId={OrgId} stage=MetricFailed " +
                "actorId={ActorId} fallbackUsed={FallbackUsed} " +
                "postsReceived={PostsReceived} postsProcessed={PostsProcessed} degradedPostCount={DegradedPostCount} " +
                "errorCode={ErrorCode}.",
                analysis.Id,
                orgId,
                scrape.UsedActorId,
                scrape.FallbackUsed,
                scrape.ProviderAggregates.PostsReceived,
                scrape.ProviderAggregates.PostsProcessed,
                scrape.ProviderAggregates.DegradedPostCount,
                statsResult.Error.Code);

            await PersistFailureWithDiagnosticsAsync(
                analysis,
                statsResult.Error.Code,
                null,
                BuildFailureDiagnostics(
                    actorId: scrape.UsedActorId,
                    fallbackUsed: scrape.FallbackUsed,
                    postsReceived: scrape.ProviderAggregates.PostsReceived,
                    postsProcessed: scrape.ProviderAggregates.PostsProcessed,
                    degradedPostCount: scrape.ProviderAggregates.DegradedPostCount,
                    aiProviderKey: null,
                    aiModel: null,
                    failureReasons: [.. scrape.Diagnostics.Select(d => d.FailureCode), statsResult.Error.Code]),
                cancellationToken);
            return;
        }

        var stats = statsResult.Value;

        // ── Stage 4: ProfileScraped|ProfileScrapedPartial ──────────────────────
        var intermediateDiagnostics = BuildIntermediateDiagnostics(
            scrape,
            stats.OwnerPostCount,
            stats.DegradedPostCount);

        analysis.CompleteScrape(scrape.IsPartial, _clock.UtcNow);
        analysis.SetRawData(
            rawPostsJson: JsonSerializer.Serialize(scrape.OwnerPosts, JsonOptions),
            statsJson: JsonSerializer.Serialize(stats, JsonOptions),
            diagnosticsJson: JsonSerializer.Serialize(intermediateDiagnostics, JsonOptions));
        await PersistAsync(analysis, cancellationToken);

        _logger.LogInformation(
            "Profile Analyzer: scrape completed " +
            "analysisId={AnalysisId} organizationId={OrgId} stage=ProfileScraped " +
            "actorId={ActorId} fallbackUsed={FallbackUsed} " +
            "postsReceived={PostsReceived} postsProcessed={PostsProcessed} degradedPostCount={DegradedPostCount}.",
            analysis.Id,
            orgId,
            scrape.UsedActorId,
            scrape.FallbackUsed,
            scrape.ProviderAggregates.PostsReceived,
            stats.OwnerPostCount,
            stats.DegradedPostCount);

        // ── Stage 5: AI provider selection ────────────────────────────────────
        var aiSelection = await _defaultAiSelector.SelectAsync(orgId, cancellationToken);
        if (aiSelection.IsFailure)
        {
            _logger.LogWarning(
                "Profile Analyzer: AI provider selection failed " +
                "analysisId={AnalysisId} organizationId={OrgId} stage=AiSelectionFailed errorCode={ErrorCode}.",
                analysis.Id,
                orgId,
                aiSelection.Error.Code);

            await PersistFailureWithDiagnosticsAsync(
                analysis,
                aiSelection.Error.Code,
                null,
                BuildFailureDiagnostics(
                    actorId: scrape.UsedActorId,
                    fallbackUsed: scrape.FallbackUsed,
                    postsReceived: scrape.ProviderAggregates.PostsReceived,
                    postsProcessed: stats.OwnerPostCount,
                    degradedPostCount: stats.DegradedPostCount,
                    aiProviderKey: null,
                    aiModel: null,
                    failureReasons: [.. scrape.Diagnostics.Select(d => d.FailureCode), aiSelection.Error.Code]),
                cancellationToken);
            return;
        }

        // ── Stage 6: Analyzing ─────────────────────────────────────────────────
        analysis.StartAnalyzing(_clock.UtcNow);
        await PersistAsync(analysis, cancellationToken);

        _logger.LogInformation(
            "Profile Analyzer: running AI analysis " +
            "analysisId={AnalysisId} organizationId={OrgId} stage=Analyzing " +
            "aiProviderKey={ProviderKey} aiModel={Model}.",
            analysis.Id,
            orgId,
            aiSelection.Value.ProviderKey,
            aiSelection.Value.Model ?? "(provider default)");

        var aiInput = CreateAiInput(scrape.Profile, stats, scrape.OwnerPosts);

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
                "Profile Analyzer: AI analysis failed " +
                "analysisId={AnalysisId} organizationId={OrgId} stage=AiFailed " +
                "actorId={ActorId} fallbackUsed={FallbackUsed} aiProviderKey={ProviderKey} errorCode={ErrorCode}.",
                analysis.Id,
                orgId,
                scrape.UsedActorId,
                scrape.FallbackUsed,
                aiSelection.Value.ProviderKey,
                aiResult.Error.Code);

            await PersistFailureWithDiagnosticsAsync(
                analysis,
                aiResult.Error.Code,
                null,
                BuildFailureDiagnostics(
                    actorId: scrape.UsedActorId,
                    fallbackUsed: scrape.FallbackUsed,
                    postsReceived: scrape.ProviderAggregates.PostsReceived,
                    postsProcessed: stats.OwnerPostCount,
                    degradedPostCount: stats.DegradedPostCount,
                    aiProviderKey: aiSelection.Value.ProviderKey,
                    aiModel: aiSelection.Value.Model,
                    failureReasons: [.. scrape.Diagnostics.Select(d => d.FailureCode), aiResult.Error.Code]),
                cancellationToken);
            return;
        }

        // ── Stage 7: Analyzed ──────────────────────────────────────────────────
        var finalDiagnostics = BuildFinalDiagnostics(
            scrape,
            stats.OwnerPostCount,
            stats.DegradedPostCount,
            aiSelection.Value.ProviderKey,
            aiSelection.Value.Model);

        var aiAnalysis = MapAiAnalysis(aiResult.Value.Payload);
        var completedAt = _clock.UtcNow;
        analysis.CompleteAnalysis(aiAnalysis, completedAt);
        analysis.SetAiAnalysis(
            aiAnalysisJson: JsonSerializer.Serialize(aiResult.Value.Payload, JsonOptions),
            diagnosticsJson: JsonSerializer.Serialize(finalDiagnostics, JsonOptions));
        await PersistAsync(analysis, cancellationToken);

        _logger.LogInformation(
            "Profile Analyzer: analysis completed successfully " +
            "analysisId={AnalysisId} organizationId={OrgId} stage=Analyzed " +
            "actorId={ActorId} fallbackUsed={FallbackUsed} " +
            "postsReceived={PostsReceived} postsProcessed={PostsProcessed} degradedPostCount={DegradedPostCount} " +
            "aiProviderKey={ProviderKey} aiModel={Model}.",
            analysis.Id,
            orgId,
            scrape.UsedActorId,
            scrape.FallbackUsed,
            scrape.ProviderAggregates.PostsReceived,
            stats.OwnerPostCount,
            stats.DegradedPostCount,
            aiSelection.Value.ProviderKey,
            aiSelection.Value.Model ?? "(provider default)");
    }

    private async Task PersistAsync(
        ProfileAnalysis analysis,
        CancellationToken cancellationToken)
    {
        await _repository.UpdateAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistFailureWithDiagnosticsAsync(
        ProfileAnalysis analysis,
        string failureCode,
        string? message,
        ProfileRunDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        analysis.MarkFailed(failureCode, message, _clock.UtcNow);
        analysis.SetDiagnostics(JsonSerializer.Serialize(diagnostics, JsonOptions));
        await _repository.UpdateAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ── Diagnostics builders (PA-23) ──────────────────────────────────────────

    /// <summary>
    /// Builds a partial/intermediate diagnostics snapshot after scrape + metric stages complete
    /// (before AI stage). Stored in SetRawData as a mid-pipeline snapshot.
    /// </summary>
    private static ProfileRunDiagnostics BuildIntermediateDiagnostics(
        ProfileScrapeResult scrape,
        int postsProcessed,
        int degradedPostCount)
    {
        var diag = new ProfileRunDiagnostics
        {
            ActorId = scrape.UsedActorId,
            FallbackUsed = scrape.FallbackUsed,
            PostsReceived = scrape.ProviderAggregates.PostsReceived,
            PostsProcessed = postsProcessed,
            DegradedPostCount = degradedPostCount,
            AiProviderKey = null,
            AiModel = null,
        };

        if (scrape.FallbackUsed && scrape.Diagnostics.Count > 0)
        {
            return diag with
            {
                DefaultActorError = scrape.Diagnostics[0].FailureCode,
                BackupActorId = scrape.UsedActorId,
            };
        }

        return diag;
    }

    /// <summary>
    /// Builds the final diagnostics snapshot for a successful (Analyzed) run.
    /// Includes full PA-23 fields including AI provider key and model.
    /// </summary>
    private static ProfileRunDiagnostics BuildFinalDiagnostics(
        ProfileScrapeResult scrape,
        int postsProcessed,
        int degradedPostCount,
        string aiProviderKey,
        string? aiModel)
    {
        var diag = new ProfileRunDiagnostics
        {
            ActorId = scrape.UsedActorId,
            FallbackUsed = scrape.FallbackUsed,
            PostsReceived = scrape.ProviderAggregates.PostsReceived,
            PostsProcessed = postsProcessed,
            DegradedPostCount = degradedPostCount,
            AiProviderKey = aiProviderKey,
            AiModel = aiModel,
        };

        if (scrape.FallbackUsed && scrape.Diagnostics.Count > 0)
        {
            return diag with
            {
                DefaultActorError = scrape.Diagnostics[0].FailureCode,
                BackupActorId = scrape.UsedActorId,
            };
        }

        return diag;
    }

    /// <summary>
    /// Builds a failure diagnostics snapshot for all Failed terminal paths (PA-23).
    /// </summary>
    private static ProfileRunDiagnostics BuildFailureDiagnostics(
        string actorId,
        bool fallbackUsed,
        int postsReceived,
        int postsProcessed,
        int degradedPostCount,
        string? aiProviderKey,
        string? aiModel,
        IReadOnlyList<string> failureReasons)
    {
        return new ProfileRunDiagnostics
        {
            ActorId = actorId,
            FallbackUsed = fallbackUsed,
            PostsReceived = postsReceived,
            PostsProcessed = postsProcessed,
            DegradedPostCount = degradedPostCount,
            AiProviderKey = aiProviderKey,
            AiModel = aiModel,
            FailureReasons = failureReasons.Count > 0 ? failureReasons : null,
        };
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
