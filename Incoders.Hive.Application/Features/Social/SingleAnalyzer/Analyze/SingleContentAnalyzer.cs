using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;

public sealed class SingleContentAnalyzer : ISingleContentAnalyzer
{
    private const string FlowKey = "single-analyzer";
    internal const int MaxAiAnalysisFailedAttempts = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISingleContentAnalysisRepository _analyses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ISocialContentResolver _contentResolver;
    private readonly IAiCompletionGateway _aiGateway;
    private readonly IContentAnalysisProfileProvider _profileProvider;
    private readonly IContentAnalysisPreflight? _preflight;
    private readonly ProviderCallContext? _providerCallContext;
    private readonly IWorkflowConfigurationResolver? _workflowResolver;
    private readonly ILogger<SingleContentAnalyzer> _logger;

    public SingleContentAnalyzer(
        ISingleContentAnalysisRepository analyses,
        IUnitOfWork unitOfWork,
        IClock clock,
        ISocialContentResolver contentResolver,
        IAiCompletionGateway aiGateway,
        IContentAnalysisProfileProvider profileProvider,
        IContentAnalysisPreflight? preflight = null,
        ProviderCallContext? providerCallContext = null,
        IWorkflowConfigurationResolver? workflowResolver = null,
        ILogger<SingleContentAnalyzer>? logger = null)
    {
        _analyses = analyses;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _contentResolver = contentResolver;
        _aiGateway = aiGateway;
        _profileProvider = profileProvider;
        _preflight = preflight;
        _providerCallContext = providerCallContext;
        _workflowResolver = workflowResolver;
        _logger = logger ?? NullLogger<SingleContentAnalyzer>.Instance;
    }

    public async Task<Result<SingleContentAnalysisResponse>> AnalyzeAsync(
        SingleContentAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, runPreflight: true, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<SingleContentAnalysisResponse>(prepared.Error);
        }

        var analysis = CreateInitialRecord(request, prepared.Value.SourceUrl);
        await _analyses.AddAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await RunCoreAsync(analysis, request, prepared.Value, cancellationToken);
    }

    public async Task<Result<SingleAnalyzerStatusResponse>> StartAsync(
        SingleContentAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, runPreflight: true, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<SingleAnalyzerStatusResponse>(prepared.Error);
        }

        var analysis = CreateInitialRecord(request, prepared.Value.SourceUrl);
        await _analyses.AddAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Single Analyzer: queued analysis {AnalysisId} for {Url} (org {OrganizationId}, platform {PlatformKey}).",
            analysis.Id,
            request.Url,
            request.OrganizationId,
            prepared.Value.PlatformKey);

        return Result.Success(SingleAnalyzerStatusResponse.FromRecord(analysis));
    }

    public async Task RunQueuedAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var analysis = await _analyses.GetByIdAsync(analysisId, organizationId, cancellationToken);
        if (analysis is null)
        {
            _logger.LogWarning(
                "Single Analyzer: queued analysis {AnalysisId} not found for org {OrganizationId}.",
                analysisId,
                organizationId);
            return;
        }

        var request = new SingleContentAnalysisRequest(
            analysis.OrganizationId,
            analysis.UserId,
            analysis.SourceUrl,
            analysis.ClientId,
            PreferredExtractionProviderKey: null);

        // Pre-flight already ran at StartAsync; don't repeat it on the background run.
        var prepared = await PrepareAsync(request, runPreflight: false, cancellationToken);
        if (prepared.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer: prepare failed for queued analysis {AnalysisId}: {ErrorCode}.",
                analysisId,
                prepared.Error.Code);
            await PersistFailureAsync(analysis, prepared.Error, [], cancellationToken);
            return;
        }

        try
        {
            await RunCoreAsync(analysis, request, prepared.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Single Analyzer: queued analysis {AnalysisId} threw.", analysisId);
            await PersistFailureAsync(analysis, SingleAnalyzerErrors.AnalysisFailed, [], CancellationToken.None);
        }
    }

    public async Task RetryAiOnlyAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var analysis = await _analyses.GetByIdAsync(analysisId, organizationId, cancellationToken);
        if (analysis is null)
        {
            _logger.LogWarning(
                "Single Analyzer retry-AI: analysis {AnalysisId} not found for org {OrganizationId}.",
                analysisId,
                organizationId);
            return;
        }

        if (string.IsNullOrWhiteSpace(analysis.ContentSnapshotJson))
        {
            _logger.LogWarning(
                "Single Analyzer retry-AI: analysis {AnalysisId} has no persisted snapshot; cannot retry AI without re-scraping.",
                analysisId);
            await PersistFailureAsync(analysis, SingleAnalyzerErrors.AnalysisFailed, [], cancellationToken, countAiFailure: true);
            return;
        }

        var aiCandidates = await ResolveAiCandidatesAsync(
            organizationId,
            analysisType: "content-analysis",
            cancellationToken);
        if (aiCandidates.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer retry-AI: AI route resolution failed for org {OrganizationId}: {ErrorCode}.",
                organizationId,
                aiCandidates.Error.Code);
            await PersistFailureAsync(analysis, aiCandidates.Error, [], cancellationToken, countAiFailure: true);
            return;
        }

        var profileResult = _profileProvider.ForFlow(FlowKey);
        var profile = profileResult.IsSuccess ? profileResult.Value : null;
        var aiCapabilityKey = profile?.AiCapabilityKey ?? "ai-analysis";
        var promptVersionKey = profile?.PromptVersionKey ?? "single-analyzer.analysis.v1";

        analysis = await PersistStatusAsync(analysis, SingleContentAnalysisStatus.Analyzing, cancellationToken);

        _logger.LogInformation(
            "Single Analyzer retry-AI: analysis {AnalysisId} re-running {CandidateCount} configured AI route(s) with persisted snapshot — NO extraction call.",
            analysis.Id,
            aiCandidates.Value.Count);

        // Embed the persisted snapshot JSON as-is into the AI input. No extraction call.
        JsonElement snapshotElement;
        try
        {
            // Non-null verified by the IsNullOrWhiteSpace guard above; ! tells the compiler.
            using var doc = JsonDocument.Parse(analysis.ContentSnapshotJson!);
            snapshotElement = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Single Analyzer retry-AI: persisted snapshot for {AnalysisId} is not valid JSON.",
                analysis.Id);
            await PersistFailureAsync(analysis, SingleAnalyzerErrors.AnalysisFailed, [], cancellationToken, countAiFailure: true);
            return;
        }

        var inputObj = new Dictionary<string, object?>
        {
            ["instruction"] = "Analyze the social content and respond with ONLY the raw JSON object matching the expected structure. Do not wrap it in markdown code fences and do not add any text before or after the JSON.",
            ["detectedPlatformKey"] = analysis.SourcePlatformKey ?? string.Empty,
            ["normalizedUrl"] = analysis.NormalizedUrl ?? analysis.SourceUrl,
            ["snapshot"] = snapshotElement,
            ["expectedOutput"] = new SingleAnalyzerExpectedOutput(
                "summary, detectedHooks, likelyAudience, narrativeStructure, strengths, weaknesses, derivedIdeas, clientAdaptationRecommendations, confidence, warnings",
                "derivedIdeas[].score dimensions are integers from 1 to 5: novelty, clientFit, platformFit, hookQuality, feasibility, expectedEngagement",
                "confidence is a number from 0 to 1")
        };

        var inputJson = JsonSerializer.Serialize(inputObj, JsonOptions);

        var (usedAi, ai) = await RunAiWithFallbackAsync(
            organizationId,
            analysis.ClientId,
            analysis.Id,
            "content-analysis",
            aiCapabilityKey,
            promptVersionKey,
            inputJson,
            aiCandidates.Value,
            Guid.NewGuid(),
            cancellationToken);

        if (usedAi is null)
        {
            var error = ai.IsFailure ? ai.Error : SingleAnalyzerErrors.AnalysisFailed;
            _logger.LogWarning(
                "WorkflowFailed OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} AnalysisId={AnalysisId} StepKey={StepKey} ErrorCode={ErrorCode}",
                organizationId,
                FlowKey,
                "content-analysis",
                analysis.Id,
                "analyze-content",
                error.Code);
            await PersistFailureAsync(analysis, error, [], cancellationToken, countAiFailure: true);
            return;
        }

        var completedAt = _clock.UtcNow;
        analysis = analysis with
        {
            Status = SingleContentAnalysisStatus.Analyzed,
            AnalysisJson = JsonSerializer.Serialize(ai.Value.Payload, JsonOptions),
            FailureCode = null,
            FailureMessageKey = null,
            UpdatedAtUtc = completedAt,
            CompletedAtUtc = completedAt
        };

        await _analyses.UpdateAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Single Analyzer retry-AI: analysis {AnalysisId} completed successfully (provider {ProviderKey}).",
            analysis.Id,
            ai.Value.ProviderKey);
    }

    public async Task<Result<SingleAnalyzerStatusResponse>> GetAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var analysis = await _analyses.GetByIdAsync(analysisId, organizationId, cancellationToken);
        return analysis is null
            ? Result.Failure<SingleAnalyzerStatusResponse>(SingleAnalyzerErrors.NotFound)
            : Result.Success(SingleAnalyzerStatusResponse.FromRecord(analysis));
    }

    /// <summary>Validates the request, resolves the module context, and (optionally) runs pre-flight.</summary>
    private async Task<Result<AnalysisContext>> PrepareAsync(
        SingleContentAnalysisRequest request,
        bool runPreflight,
        CancellationToken cancellationToken)
    {
        var sourceUrl = SingleAnalyzerSourceUrl.Create(request.Url);
        if (sourceUrl.IsFailure)
        {
            return Result.Failure<AnalysisContext>(sourceUrl.Error);
        }

        var validation = ValidateRequest(request);
        if (validation.IsFailure)
        {
            return Result.Failure<AnalysisContext>(validation.Error);
        }

        var profileResult = _profileProvider.ForFlow(FlowKey);
        var profile = profileResult.IsSuccess ? profileResult.Value : null;

        var platformKey = profile is not null
            ? profile.DetectPlatform(sourceUrl.Value.OriginalUrl)
            : sourceUrl.Value.PlatformKey;

        var aiCapabilityKey = profile?.AiCapabilityKey ?? "ai-analysis";
        var promptVersionKey = profile?.PromptVersionKey ?? "single-analyzer.analysis.v1";
        var extractionProviderKey = request.PreferredExtractionProviderKey ?? profile?.ExtractionProviderKey ?? "apify";
        IReadOnlyList<string>? extractionActorIds = null;

        // AI provider selection comes SOLELY from the org's priority-ordered workflow configuration.
        // priority-1 is the default; the rest are runtime fallbacks tried in order if the primary fails.
        var aiCandidates = await ResolveAiCandidatesAsync(
            request.OrganizationId,
            analysisType: "content-analysis",
            cancellationToken);
        if (aiCandidates.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer: AI route resolution failed for org {OrganizationId}: {ErrorCode}.",
                request.OrganizationId,
                aiCandidates.Error.Code);
            return Result.Failure<AnalysisContext>(aiCandidates.Error);
        }

        var primaryAi = aiCandidates.Value[0];

        if (_workflowResolver is not null)
        {
            var scrapeRoute = await _workflowResolver.ResolveStepAsync(
                new WorkflowStepResolutionRequest(
                    request.OrganizationId,
                    FlowKey,
                    "content-analysis",
                    "scrape-video",
                    "scrape-video",
                    platformKey),
                cancellationToken);

            if (scrapeRoute.IsSuccess && scrapeRoute.Value.Count > 0)
            {
                extractionProviderKey = scrapeRoute.Value[0].ProviderKey;
                extractionActorIds = scrapeRoute.Value
                    .Select(x => x.ActorId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .ToList();

                _logger.LogInformation(
                    "WorkflowConfigurationResolved OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} StepKey={StepKey} NetworkKey={NetworkKey} ProviderKey={ProviderKey} ActorIds={ActorIds}",
                    request.OrganizationId,
                    FlowKey,
                    "content-analysis",
                    "scrape-video",
                    platformKey,
                    extractionProviderKey,
                    string.Join(",", extractionActorIds));
            }
        }

        // PRE-FLIGHT (hard prerequisite): every external API must be reachable before we start.
        if (runPreflight && _preflight is not null)
        {
            var preflight = await _preflight.EnsureReadyAsync(
                request.OrganizationId,
                extractionProviderKey,
                primaryAi.ProviderKey,
                cancellationToken);

            if (preflight.IsFailure)
            {
                _logger.LogWarning(
                    "Single Analyzer: pre-flight failed for org {OrganizationId} (extraction={ExtractionProvider}, ai={AiProvider}): {ErrorCode}. Workflow not started.",
                    request.OrganizationId,
                    extractionProviderKey,
                    primaryAi.ProviderKey,
                    preflight.Error.Code);
                return Result.Failure<AnalysisContext>(preflight.Error);
            }
        }

        return Result.Success(new AnalysisContext(
            sourceUrl.Value,
            platformKey,
            extractionProviderKey,
            aiCapabilityKey,
            promptVersionKey,
            aiCandidates.Value,
            extractionActorIds));
    }

    /// <summary>Runs extraction + AI on an already-created record, persisting status as it goes.</summary>
    private async Task<Result<SingleContentAnalysisResponse>> RunCoreAsync(
        SingleContentAnalysisRecord analysis,
        SingleContentAnalysisRequest request,
        AnalysisContext ctx,
        CancellationToken cancellationToken)
    {
        // One correlation ID per analysis run — stitches all ledger rows for this job.
        var correlationId = Guid.NewGuid();

        analysis = await PersistStatusAsync(analysis, SingleContentAnalysisStatus.ResolvingContent, cancellationToken);

        _logger.LogInformation(
            "Single Analyzer: starting analysis {AnalysisId} for {Url} (org {OrganizationId}, platform {PlatformKey}).",
            analysis.Id,
            request.Url,
            request.OrganizationId,
            ctx.PlatformKey);

        _logger.LogInformation(
            "WorkflowStart OrganizationId={OrganizationId} UserId={UserId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} NetworkKey={NetworkKey} AnalysisId={AnalysisId} CorrelationId={CorrelationId}",
            request.OrganizationId,
            request.UserId,
            FlowKey,
            "content-analysis",
            ctx.PlatformKey,
            analysis.Id,
            correlationId);

        Result<SocialContentResolution> content;
        using (_providerCallContext?.Begin(
                   request.OrganizationId,
                   request.ClientId,
                   workflowKey: FlowKey,
                   pipelineStepKey: "content-resolution",
                   promptVersion: null,
                   correlationId))
        {
            content = await _contentResolver.ResolveAsync(
                new SocialContentResolutionRequest(
                    request.OrganizationId,
                    analysis.Id,
                    request.ClientId,
                    ctx.SourceUrl.OriginalUrl,
                    ctx.PlatformKey,
                    ctx.ExtractionProviderKey,
                    ctx.ExtractionActorIds),
                cancellationToken);
        }

        if (content.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer: content resolution failed for analysis {AnalysisId}: {ErrorCode}.",
                analysis.Id,
                content.Error.Code);
            await PersistFailureAsync(analysis, content.Error, [], cancellationToken);
            return Result.Failure<SingleContentAnalysisResponse>(content.Error);
        }

        var snapshot = MapSnapshot(content.Value.Snapshot);
        if (snapshot.IsFailure)
        {
            await PersistFailureAsync(analysis, snapshot.Error, content.Value.Diagnostics, cancellationToken);
            return Result.Failure<SingleContentAnalysisResponse>(snapshot.Error);
        }

        var contentStatus = content.Value.Snapshot.Warnings.Count == 0
            ? SingleContentAnalysisStatus.ContentResolved
            : SingleContentAnalysisStatus.ContentResolvedPartial;

        analysis = analysis with
        {
            Status = contentStatus,
            SourcePlatformKey = snapshot.Value.PlatformKey,
            NormalizedUrl = ctx.SourceUrl.NormalizedUrl,
            ContentSnapshotJson = JsonSerializer.Serialize(snapshot.Value, JsonOptions),
            ProviderDiagnosticsJson = JsonSerializer.Serialize(content.Value.Diagnostics, JsonOptions),
            UpdatedAtUtc = _clock.UtcNow
        };
        await _analyses.UpdateAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        analysis = await PersistStatusAsync(analysis, SingleContentAnalysisStatus.Analyzing, cancellationToken);

        var aiInputJson = JsonSerializer.Serialize(CreateAiInput(ctx.SourceUrl, content.Value.Snapshot), JsonOptions);
        var (usedAi, ai) = await RunAiWithFallbackAsync(
            request.OrganizationId,
            request.ClientId,
            analysis.Id,
            "content-analysis",
            ctx.AiCapabilityKey,
            ctx.PromptVersionKey,
            aiInputJson,
            ctx.AiCandidates,
            correlationId,
            cancellationToken);

        if (usedAi is null)
        {
            var error = ai.IsFailure ? ai.Error : SingleAnalyzerErrors.AnalysisFailed;
            _logger.LogWarning(
                "WorkflowFailed OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} AnalysisId={AnalysisId} StepKey={StepKey} ErrorCode={ErrorCode}",
                request.OrganizationId,
                FlowKey,
                "content-analysis",
                analysis.Id,
                "analyze-content",
                error.Code);

            await PersistFailureAsync(
                analysis,
                error,
                MergeDiagnostics(content.Value.Diagnostics, ai.IsSuccess ? ai.Value.Diagnostics : Array.Empty<ProviderFailureDiagnostic>()),
                cancellationToken,
                countAiFailure: true);
            return Result.Failure<SingleContentAnalysisResponse>(error);
        }

        var diagnostics = MergeDiagnostics(content.Value.Diagnostics, ai.Value.Diagnostics);
        var completedAt = _clock.UtcNow;
        analysis = analysis with
        {
            Status = SingleContentAnalysisStatus.Analyzed,
            AnalysisJson = JsonSerializer.Serialize(ai.Value.Payload, JsonOptions),
            ProviderDiagnosticsJson = JsonSerializer.Serialize(diagnostics, JsonOptions),
            FailureCode = null,
            FailureMessageKey = null,
            UpdatedAtUtc = completedAt,
            CompletedAtUtc = completedAt
        };

        await _analyses.UpdateAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "WorkflowCompleted OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} AnalysisId={AnalysisId} ExtractionProviderKey={ExtractionProviderKey} AiProviderKey={AiProviderKey} AiModel={AiModel} AiPriority={AiPriority} AiFallbackUsed={AiFallbackUsed}",
            request.OrganizationId,
            FlowKey,
            "content-analysis",
            analysis.Id,
            ctx.ExtractionProviderKey,
            ai.Value.ProviderKey,
            ai.Value.Model ?? "(provider default)",
            usedAi.Priority,
            usedAi.Priority > 1);

        _logger.LogInformation(
            "Single Analyzer: analysis {AnalysisId} completed successfully (provider {ProviderKey}).",
            analysis.Id,
            ai.Value.ProviderKey);

        return Result.Success(new SingleContentAnalysisResponse(
            analysis.Id,
            analysis.OrganizationId,
            analysis.ClientId,
            analysis.UserId,
            analysis.SourceUrl,
            analysis.NormalizedUrl ?? ctx.SourceUrl.NormalizedUrl,
            snapshot.Value.PlatformKey,
            snapshot.Value.CanonicalUrl,
            analysis.Status,
            SingleAnalyzerContentSnapshotResponse.FromDomain(snapshot.Value),
            SingleAnalyzerAnalysisResponse.FromDomain(ai.Value.Payload),
            content.Value.Snapshot.Warnings.Select(SingleAnalyzerWarningResponse.FromContentWarning).ToArray(),
            diagnostics,
            ai.Value.ProviderKey,
            ai.Value.Model,
            analysis.CreatedAtUtc,
            analysis.UpdatedAtUtc,
            analysis.CompletedAtUtc));
    }

    private sealed record AnalysisContext(
        SingleAnalyzerSourceUrl SourceUrl,
        string PlatformKey,
            string ExtractionProviderKey,
            string AiCapabilityKey,
            string PromptVersionKey,
            IReadOnlyList<AiRoute> AiCandidates,
            IReadOnlyList<string>? ExtractionActorIds);

    private async Task<Result<IReadOnlyList<AiRoute>>> ResolveAiCandidatesAsync(
        Guid organizationId,
        string analysisType,
        CancellationToken cancellationToken)
    {
        // AI selection comes SOLELY from the organization's priority-ordered workflow configuration.
        // There is no Default-AI fallback anymore: priority-1 IS the default and admins define it.
        if (_workflowResolver is null)
        {
            return Result.Failure<IReadOnlyList<AiRoute>>(IntegrationErrors.AiRouteNotConfigured);
        }

        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                organizationId,
                FlowKey,
                analysisType,
                "analyze-content",
                "ai-analysis",
                null),
            cancellationToken);

        if (route.IsFailure)
        {
            return Result.Failure<IReadOnlyList<AiRoute>>(route.Error);
        }

        var candidates = route.Value
            .Where(candidate => candidate.IsEnabled)
            .OrderBy(candidate => candidate.Priority)
            .Select(candidate => new AiRoute(candidate.ProviderKey, candidate.ModelId, candidate.Priority))
            .ToList();

        if (candidates.Count == 0)
        {
            return Result.Failure<IReadOnlyList<AiRoute>>(IntegrationErrors.AiRouteNotConfigured);
        }

        foreach (var candidate in candidates)
        {
            _logger.LogInformation(
                "WorkflowStepResolved OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} StepKey={StepKey} ProviderKey={ProviderKey} ModelId={ModelId} Priority={Priority} IsFallback={IsFallback}",
                organizationId,
                FlowKey,
                analysisType,
                "analyze-content",
                candidate.ProviderKey,
                candidate.ModelId ?? "(provider default)",
                candidate.Priority,
                candidate.Priority > 1);
        }

        return Result.Success<IReadOnlyList<AiRoute>>(candidates);
    }

    /// <summary>
    /// Runs the AI completion trying each configured route in priority order. On failure of a route,
    /// logs WorkflowFallbackSelected / WorkflowProviderAttemptFailed and tries the next one. Returns the
    /// used route + successful response, or (null, last error) when every configured route failed.
    /// </summary>
    private async Task<(AiRoute? Used, Result<AiJsonCompletionResponse<SingleAnalyzerAnalysis>> Result)> RunAiWithFallbackAsync(
        Guid organizationId,
        Guid? clientId,
        Guid analysisId,
        string analysisType,
        string aiCapabilityKey,
        string promptVersionKey,
        string inputJson,
        IReadOnlyList<AiRoute> candidates,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var last = Result.Failure<AiJsonCompletionResponse<SingleAnalyzerAnalysis>>(SingleAnalyzerErrors.AnalysisFailed);

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var isFallback = index > 0;

            if (isFallback)
            {
                _logger.LogInformation(
                    "WorkflowFallbackSelected OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} StepKey={StepKey} ProviderKey={ProviderKey} ModelId={ModelId} Priority={Priority}",
                    organizationId,
                    FlowKey,
                    analysisType,
                    "analyze-content",
                    candidate.ProviderKey,
                    candidate.ModelId ?? "(provider default)",
                    candidate.Priority);
            }

            _logger.LogInformation(
                "WorkflowProviderAttemptStart OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} StepKey={StepKey} ProviderKey={ProviderKey} ModelId={ModelId} Priority={Priority} IsFallback={IsFallback}",
                organizationId,
                FlowKey,
                analysisType,
                "analyze-content",
                candidate.ProviderKey,
                candidate.ModelId ?? "(provider default)",
                candidate.Priority,
                isFallback);

            Result<AiJsonCompletionResponse<SingleAnalyzerAnalysis>> ai;
            try
            {
                using (_providerCallContext?.Begin(
                           organizationId,
                           clientId,
                           workflowKey: FlowKey,
                           pipelineStepKey: "ai-analysis",
                           promptVersion: promptVersionKey,
                           correlationId))
                {
                    ai = await _aiGateway.CompleteJsonAsync<SingleAnalyzerAnalysis>(
                        new AiJsonCompletionRequest(
                            organizationId,
                            analysisId,
                            aiCapabilityKey,
                            promptVersionKey,
                            inputJson,
                            candidate.ProviderKey,
                            candidate.ModelId),
                        cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Single Analyzer: AI analysis threw for analysis {AnalysisId} (provider {ProviderKey}).", analysisId, candidate.ProviderKey);
                last = Result.Failure<AiJsonCompletionResponse<SingleAnalyzerAnalysis>>(SingleAnalyzerErrors.AnalysisFailed);
                LogAiAttemptFailed(organizationId, analysisType, candidate, isFallback, last.Error.Code);
                continue;
            }

            if (ai.IsSuccess)
            {
                var validation = ai.Value.Payload.Validate();
                if (validation.IsSuccess)
                {
                    _logger.LogInformation(
                        "WorkflowProviderAttemptSucceeded OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} StepKey={StepKey} ProviderKey={ProviderKey} ModelId={ModelId} Priority={Priority} IsFallback={IsFallback}",
                        organizationId,
                        FlowKey,
                        analysisType,
                        "analyze-content",
                        ai.Value.ProviderKey,
                        ai.Value.Model ?? "(provider default)",
                        candidate.Priority,
                        isFallback);

                    return (candidate, ai);
                }

                last = Result.Failure<AiJsonCompletionResponse<SingleAnalyzerAnalysis>>(validation.Error);
            }
            else
            {
                last = ai;
            }

            LogAiAttemptFailed(organizationId, analysisType, candidate, isFallback, last.Error.Code);
        }

        return (null, last);
    }

    private void LogAiAttemptFailed(Guid organizationId, string analysisType, AiRoute candidate, bool isFallback, string errorCode) =>
        _logger.LogWarning(
            "WorkflowProviderAttemptFailed OrganizationId={OrganizationId} WorkflowKey={WorkflowKey} AnalysisType={AnalysisType} StepKey={StepKey} ProviderKey={ProviderKey} ModelId={ModelId} Priority={Priority} IsFallback={IsFallback} ErrorCode={ErrorCode}",
            organizationId,
            FlowKey,
            analysisType,
            "analyze-content",
            candidate.ProviderKey,
            candidate.ModelId ?? "(provider default)",
            candidate.Priority,
            isFallback,
            errorCode);

    private sealed record AiRoute(string ProviderKey, string? ModelId, int Priority);

    private static Result ValidateRequest(SingleContentAnalysisRequest request)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure(SingleAnalyzerErrors.OrganizationRequired);
        }

        if (request.UserId == Guid.Empty)
        {
            return Result.Failure(SingleAnalyzerErrors.UserRequired);
        }

        return Result.Success();
    }

    private SingleContentAnalysisRecord CreateInitialRecord(
        SingleContentAnalysisRequest request,
        SingleAnalyzerSourceUrl sourceUrl)
    {
        var now = _clock.UtcNow;
        return new SingleContentAnalysisRecord(
            Id: Guid.NewGuid(),
            OrganizationId: request.OrganizationId,
            ClientId: request.ClientId,
            UserId: request.UserId,
            SourceUrl: sourceUrl.OriginalUrl,
            NormalizedUrl: sourceUrl.NormalizedUrl,
            SourcePlatformKey: sourceUrl.PlatformKey,
            Status: SingleContentAnalysisStatus.Requested,
            ContentSnapshotJson: null,
            AnalysisJson: null,
            ProviderDiagnosticsJson: null,
            FailureCode: null,
            FailureMessageKey: null,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            CompletedAtUtc: null);
    }

    private async Task<SingleContentAnalysisRecord> PersistStatusAsync(
        SingleContentAnalysisRecord analysis,
        SingleContentAnalysisStatus status,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var updated = analysis with
        {
            Status = status,
            UpdatedAtUtc = now,
            CompletedAtUtc = status is SingleContentAnalysisStatus.Analyzed or SingleContentAnalysisStatus.Failed
                ? now
                : analysis.CompletedAtUtc
        };

        await _analyses.UpdateAsync(updated, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return updated;
    }

    private async Task PersistFailureAsync(
        SingleContentAnalysisRecord analysis,
        Error error,
        IReadOnlyList<ProviderFailureDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        bool countAiFailure = false)
    {
        var now = _clock.UtcNow;
        var aiFailedAttempts = countAiFailure
            ? analysis.AiAnalysisFailedAttemptCount + 1
            : analysis.AiAnalysisFailedAttemptCount;

        var failed = analysis with
        {
            Status = SingleContentAnalysisStatus.Failed,
            ProviderDiagnosticsJson = JsonSerializer.Serialize(diagnostics, JsonOptions),
            FailureCode = error.Code,
            FailureMessageKey = error.MessageKey,
            UpdatedAtUtc = now,
            CompletedAtUtc = now,
            AiAnalysisFailedAttemptCount = aiFailedAttempts,
            AiAnalysisRetryBlockedAtUtc = countAiFailure
                && aiFailedAttempts >= MaxAiAnalysisFailedAttempts
                && analysis.AiAnalysisRetryBlockedAtUtc is null
                    ? now
                    : analysis.AiAnalysisRetryBlockedAtUtc
        };

        await _analyses.UpdateAsync(failed, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static Result<SingleAnalyzerContentSnapshot> MapSnapshot(SocialContentSnapshot snapshot) =>
        SingleAnalyzerContentSnapshot.Create(
            snapshot.PlatformKey,
            snapshot.CanonicalUrl,
            snapshot.Title,
            snapshot.Description,
            snapshot.Author,
            snapshot.ThumbnailUrl,
            snapshot.DurationSeconds,
            snapshot.Metrics,
            snapshot.Transcript,
            snapshot.MediaReferences
                .Select(x => new SingleAnalyzerMediaReference(x.Kind, x.Uri, x.MimeType))
                .ToArray(),
            snapshot.RawPayloadReference,
            snapshot.Tags,
            snapshot.ContentType,
            snapshot.PublishedAtUtc);

    private static IReadOnlyList<ProviderFailureDiagnostic> MergeDiagnostics(
        IReadOnlyList<ProviderFailureDiagnostic> first,
        IReadOnlyList<ProviderFailureDiagnostic> second) =>
        first.Concat(second).ToArray();

    private static SingleAnalyzerAiInput CreateAiInput(
        SingleAnalyzerSourceUrl sourceUrl,
        SocialContentSnapshot snapshot) =>
        new(
            "Analyze the social content and respond with ONLY the raw JSON object matching the expected structure. Do not wrap it in markdown code fences and do not add any text before or after the JSON.",
            sourceUrl.PlatformKey,
            sourceUrl.NormalizedUrl,
            snapshot,
            new SingleAnalyzerExpectedOutput(
                "summary, detectedHooks, likelyAudience, narrativeStructure, strengths, weaknesses, derivedIdeas, clientAdaptationRecommendations, confidence, warnings",
                "derivedIdeas[].score dimensions are integers from 1 to 5: novelty, clientFit, platformFit, hookQuality, feasibility, expectedEngagement",
                "confidence is a number from 0 to 1"));

    private sealed record SingleAnalyzerAiInput(
        string Instruction,
        string DetectedPlatformKey,
        string NormalizedUrl,
        SocialContentSnapshot Snapshot,
        SingleAnalyzerExpectedOutput ExpectedOutput);

    private sealed record SingleAnalyzerExpectedOutput(
        string RequiredFields,
        string IdeaScoring,
        string Confidence);
}
