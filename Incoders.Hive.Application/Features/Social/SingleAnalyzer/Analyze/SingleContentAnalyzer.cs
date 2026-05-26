using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISingleContentAnalysisRepository _analyses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ISocialContentResolver _contentResolver;
    private readonly IAiCompletionGateway _aiGateway;
    private readonly IDefaultAiSelector _defaultAiSelector;
    private readonly IContentAnalysisProfileProvider _profileProvider;
    private readonly IContentAnalysisPreflight? _preflight;
    private readonly ProviderCallContext? _providerCallContext;
    private readonly ILogger<SingleContentAnalyzer> _logger;

    public SingleContentAnalyzer(
        ISingleContentAnalysisRepository analyses,
        IUnitOfWork unitOfWork,
        IClock clock,
        ISocialContentResolver contentResolver,
        IAiCompletionGateway aiGateway,
        IDefaultAiSelector defaultAiSelector,
        IContentAnalysisProfileProvider profileProvider,
        IContentAnalysisPreflight? preflight = null,
        ProviderCallContext? providerCallContext = null,
        ILogger<SingleContentAnalyzer>? logger = null)
    {
        _analyses = analyses;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _contentResolver = contentResolver;
        _aiGateway = aiGateway;
        _defaultAiSelector = defaultAiSelector;
        _profileProvider = profileProvider;
        _preflight = preflight;
        _providerCallContext = providerCallContext;
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
            await PersistFailureAsync(analysis, SingleAnalyzerErrors.AnalysisFailed, [], cancellationToken);
            return;
        }

        var selectedAi = await _defaultAiSelector.SelectAsync(organizationId, cancellationToken);
        if (selectedAi.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer retry-AI: AI provider selection failed for org {OrganizationId}: {ErrorCode}.",
                organizationId,
                selectedAi.Error.Code);
            await PersistFailureAsync(analysis, selectedAi.Error, [], cancellationToken);
            return;
        }

        var profileResult = _profileProvider.ForFlow(FlowKey);
        var profile = profileResult.IsSuccess ? profileResult.Value : null;
        var aiCapabilityKey = profile?.AiCapabilityKey ?? "ai-analysis";
        var promptVersionKey = profile?.PromptVersionKey ?? "single-analyzer.analysis.v1";

        analysis = await PersistStatusAsync(analysis, SingleContentAnalysisStatus.Analyzing, cancellationToken);

        _logger.LogInformation(
            "Single Analyzer retry-AI: analysis {AnalysisId} re-running AI provider {ProviderKey} (model {Model}) with persisted snapshot — NO extraction call.",
            analysis.Id,
            selectedAi.Value.ProviderKey,
            selectedAi.Value.Model ?? "(provider default)");

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
            await PersistFailureAsync(analysis, SingleAnalyzerErrors.AnalysisFailed, [], cancellationToken);
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

        Result<AiJsonCompletionResponse<SingleAnalyzerAnalysis>> ai;
        try
        {
            ai = await _aiGateway.CompleteJsonAsync<SingleAnalyzerAnalysis>(
                new AiJsonCompletionRequest(
                    organizationId,
                    analysis.Id,
                    aiCapabilityKey,
                    promptVersionKey,
                    inputJson,
                    selectedAi.Value.ProviderKey,
                    selectedAi.Value.Model),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Single Analyzer retry-AI: analysis {AnalysisId} threw.", analysis.Id);
            await PersistFailureAsync(analysis, SingleAnalyzerErrors.AnalysisFailed, [], CancellationToken.None);
            return;
        }

        if (ai.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer retry-AI: AI analysis failed for {AnalysisId} (provider {ProviderKey}): {ErrorCode}.",
                analysis.Id,
                selectedAi.Value.ProviderKey,
                ai.Error.Code);
            await PersistFailureAsync(analysis, ai.Error, [], cancellationToken);
            return;
        }

        var aiValidation = ai.Value.Payload.Validate();
        if (aiValidation.IsFailure)
        {
            await PersistFailureAsync(analysis, aiValidation.Error, [], cancellationToken);
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
            selectedAi.Value.ProviderKey);
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

        // AI provider selection is org-integration-driven (CAP3-MUST-04): is_default_ai → last configured.
        var selectedAi = await _defaultAiSelector.SelectAsync(request.OrganizationId, cancellationToken);
        if (selectedAi.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer: AI provider selection failed for org {OrganizationId}: {ErrorCode}.",
                request.OrganizationId,
                selectedAi.Error.Code);
            return Result.Failure<AnalysisContext>(selectedAi.Error);
        }

        // PRE-FLIGHT (hard prerequisite): every external API must be reachable before we start.
        if (runPreflight && _preflight is not null)
        {
            var preflight = await _preflight.EnsureReadyAsync(
                request.OrganizationId,
                extractionProviderKey,
                selectedAi.Value.ProviderKey,
                cancellationToken);

            if (preflight.IsFailure)
            {
                _logger.LogWarning(
                    "Single Analyzer: pre-flight failed for org {OrganizationId} (extraction={ExtractionProvider}, ai={AiProvider}): {ErrorCode}. Workflow not started.",
                    request.OrganizationId,
                    extractionProviderKey,
                    selectedAi.Value.ProviderKey,
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
            selectedAi.Value.ProviderKey,
            selectedAi.Value.Model));
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
                    ctx.ExtractionProviderKey),
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

        _logger.LogInformation(
            "Single Analyzer: analysis {AnalysisId} using AI provider {ProviderKey} (model {Model}).",
            analysis.Id,
            ctx.AiProviderKey,
            ctx.AiModel ?? "(provider default)");

        Result<AiJsonCompletionResponse<SingleAnalyzerAnalysis>> ai;
        using (_providerCallContext?.Begin(
                   request.OrganizationId,
                   request.ClientId,
                   workflowKey: FlowKey,
                   pipelineStepKey: "ai-analysis",
                   promptVersion: ctx.PromptVersionKey,
                   correlationId))
        {
            ai = await _aiGateway.CompleteJsonAsync<SingleAnalyzerAnalysis>(
                new AiJsonCompletionRequest(
                    request.OrganizationId,
                    analysis.Id,
                    ctx.AiCapabilityKey,
                    ctx.PromptVersionKey,
                    JsonSerializer.Serialize(CreateAiInput(ctx.SourceUrl, content.Value.Snapshot), JsonOptions),
                    ctx.AiProviderKey,
                    ctx.AiModel),
                cancellationToken);
        }

        if (ai.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer: AI analysis failed for analysis {AnalysisId} (provider {ProviderKey}): {ErrorCode}.",
                analysis.Id,
                ctx.AiProviderKey,
                ai.Error.Code);
            await PersistFailureAsync(analysis, ai.Error, content.Value.Diagnostics, cancellationToken);
            return Result.Failure<SingleContentAnalysisResponse>(ai.Error);
        }

        var aiValidation = ai.Value.Payload.Validate();
        if (aiValidation.IsFailure)
        {
            await PersistFailureAsync(
                analysis,
                aiValidation.Error,
                MergeDiagnostics(content.Value.Diagnostics, ai.Value.Diagnostics),
                cancellationToken);
            return Result.Failure<SingleContentAnalysisResponse>(aiValidation.Error);
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
            "Single Analyzer: analysis {AnalysisId} completed successfully (provider {ProviderKey}).",
            analysis.Id,
            ctx.AiProviderKey);

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
        string AiProviderKey,
        string? AiModel);

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
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var failed = analysis with
        {
            Status = SingleContentAnalysisStatus.Failed,
            ProviderDiagnosticsJson = JsonSerializer.Serialize(diagnostics, JsonOptions),
            FailureCode = error.Code,
            FailureMessageKey = error.MessageKey,
            UpdatedAtUtc = now,
            CompletedAtUtc = now
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
