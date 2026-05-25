using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
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
    private readonly ILogger<SingleContentAnalyzer> _logger;

    public SingleContentAnalyzer(
        ISingleContentAnalysisRepository analyses,
        IUnitOfWork unitOfWork,
        IClock clock,
        ISocialContentResolver contentResolver,
        IAiCompletionGateway aiGateway,
        IDefaultAiSelector defaultAiSelector,
        IContentAnalysisProfileProvider profileProvider,
        ILogger<SingleContentAnalyzer>? logger = null)
    {
        _analyses = analyses;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _contentResolver = contentResolver;
        _aiGateway = aiGateway;
        _defaultAiSelector = defaultAiSelector;
        _profileProvider = profileProvider;
        _logger = logger ?? NullLogger<SingleContentAnalyzer>.Instance;
    }

    public async Task<Result<SingleContentAnalysisResponse>> AnalyzeAsync(
        SingleContentAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var sourceUrl = SingleAnalyzerSourceUrl.Create(request.Url);
        if (sourceUrl.IsFailure)
        {
            return Result.Failure<SingleContentAnalysisResponse>(sourceUrl.Error);
        }

        var validation = ValidateRequest(request);
        if (validation.IsFailure)
        {
            return Result.Failure<SingleContentAnalysisResponse>(validation.Error);
        }

        // Resolve the module profile — provides capability keys, prompt version, and platform detection.
        var profileResult = _profileProvider.ForFlow(FlowKey);
        var profile = profileResult.IsSuccess ? profileResult.Value : null;

        // Use profile for platform detection if available; otherwise fall back to domain value object.
        var platformKey = profile is not null
            ? profile.DetectPlatform(sourceUrl.Value.OriginalUrl)
            : sourceUrl.Value.PlatformKey;

        var aiCapabilityKey = profile?.AiCapabilityKey ?? "ai-analysis";
        var promptVersionKey = profile?.PromptVersionKey ?? "single-analyzer.analysis.v1";

        var analysis = CreateInitialRecord(request, sourceUrl.Value);
        await _analyses.AddAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        analysis = await PersistStatusAsync(
            analysis,
            SingleContentAnalysisStatus.ResolvingContent,
            cancellationToken);

        _logger.LogInformation(
            "Single Analyzer: starting analysis {AnalysisId} for {Url} (org {OrganizationId}, platform {PlatformKey}).",
            analysis.Id,
            request.Url,
            request.OrganizationId,
            platformKey);

        var content = await _contentResolver.ResolveAsync(
            new SocialContentResolutionRequest(
                request.OrganizationId,
                analysis.Id,
                request.ClientId,
                sourceUrl.Value.OriginalUrl,
                platformKey,
                request.PreferredExtractionProviderKey ?? profile?.ExtractionProviderKey),
            cancellationToken);

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
            NormalizedUrl = sourceUrl.Value.NormalizedUrl,
            ContentSnapshotJson = JsonSerializer.Serialize(snapshot.Value, JsonOptions),
            ProviderDiagnosticsJson = JsonSerializer.Serialize(content.Value.Diagnostics, JsonOptions),
            UpdatedAtUtc = _clock.UtcNow
        };
        await _analyses.UpdateAsync(analysis, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        analysis = await PersistStatusAsync(
            analysis,
            SingleContentAnalysisStatus.Analyzing,
            cancellationToken);

        // AI provider selection is always org-integration-driven (CAP3-MUST-04):
        // is_default_ai first, else the last-configured AI integration for the org.
        var selectedAi = await _defaultAiSelector.SelectAsync(request.OrganizationId, cancellationToken);
        if (selectedAi.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer: AI provider selection failed for analysis {AnalysisId}: {ErrorCode}.",
                analysis.Id,
                selectedAi.Error.Code);
            await PersistFailureAsync(analysis, selectedAi.Error, content.Value.Diagnostics, cancellationToken);
            return Result.Failure<SingleContentAnalysisResponse>(selectedAi.Error);
        }

        var aiProviderKey = selectedAi.Value.ProviderKey;
        var aiModel = selectedAi.Value.Model;

        _logger.LogInformation(
            "Single Analyzer: analysis {AnalysisId} using AI provider {ProviderKey} (model {Model}).",
            analysis.Id,
            aiProviderKey,
            aiModel ?? "(provider default)");

        var ai = await _aiGateway.CompleteJsonAsync<SingleAnalyzerAnalysis>(
            new AiJsonCompletionRequest(
                request.OrganizationId,
                analysis.Id,
                aiCapabilityKey,
                promptVersionKey,
                JsonSerializer.Serialize(CreateAiInput(sourceUrl.Value, content.Value.Snapshot), JsonOptions),
                aiProviderKey,
                aiModel),
            cancellationToken);

        if (ai.IsFailure)
        {
            _logger.LogWarning(
                "Single Analyzer: AI analysis failed for analysis {AnalysisId} (provider {ProviderKey}): {ErrorCode}.",
                analysis.Id,
                aiProviderKey,
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
            aiProviderKey);

        return Result.Success(new SingleContentAnalysisResponse(
            analysis.Id,
            analysis.OrganizationId,
            analysis.ClientId,
            analysis.UserId,
            analysis.SourceUrl,
            analysis.NormalizedUrl ?? sourceUrl.Value.NormalizedUrl,
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
            "Analyze the social content and return only JSON matching the expected structured output.",
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
