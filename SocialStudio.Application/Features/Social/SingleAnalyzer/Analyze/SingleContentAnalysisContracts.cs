using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.SingleAnalyzer;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Application.Features.Social.SingleAnalyzer.Analyze;

public interface ISingleContentAnalyzer
{
    /// <summary>Full synchronous run (kept for internal use and tests).</summary>
    Task<Result<SingleContentAnalysisResponse>> AnalyzeAsync(
        SingleContentAnalysisRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates + runs pre-flight + creates the analysis record (status Requested) and returns it.
    /// The caller then enqueues a background job; the heavy work runs via <see cref="RunQueuedAsync"/>.
    /// </summary>
    Task<Result<SingleAnalyzerStatusResponse>> StartAsync(
        SingleContentAnalysisRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Background: loads the record and runs extraction + AI, persisting status as it goes.</summary>
    Task RunQueuedAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Background: re-runs ONLY the AI stage using the already-persisted content snapshot.
    /// Skips extraction entirely — no Apify call, no extra tokens — assuming the snapshot we
    /// already obtained is still correct. Used when the previous run failed at the AI stage.
    /// </summary>
    Task RetryAiOnlyAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an analysis (for polling).</summary>
    Task<Result<SingleAnalyzerStatusResponse>> GetAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

public sealed record SingleContentAnalysisRequest(
    Guid OrganizationId,
    Guid UserId,
    string Url,
    Guid? ClientId,
    string? PreferredExtractionProviderKey);

public sealed record SingleContentAnalysisResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    Guid UserId,
    string SourceUrl,
    string NormalizedUrl,
    string PlatformKey,
    string CanonicalUrl,
    SingleContentAnalysisStatus Status,
    SingleAnalyzerContentSnapshotResponse Snapshot,
    SingleAnalyzerAnalysisResponse Analysis,
    IReadOnlyList<SingleAnalyzerWarningResponse> Warnings,
    IReadOnlyList<ProviderFailureDiagnostic> Diagnostics,
    string AiProviderKey,
    string? AiModel,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);

/// <summary>
/// Poll/status view of an analysis. Snapshot/Analysis are null until those stages complete;
/// FailureCode/MessageKey are set when the analysis fails. Built from the persisted record.
/// </summary>
public sealed record SingleAnalyzerStatusResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    string SourceUrl,
    string? NormalizedUrl,
    string PlatformKey,
    SingleContentAnalysisStatus Status,
    SingleAnalyzerContentSnapshotResponse? Snapshot,
    SingleAnalyzerAnalysisResponse? Analysis,
    string? FailureCode,
    string? FailureMessageKey,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    int AiAnalysisFailedAttemptCount = 0,
    bool IsAiAnalysisRetryBlocked = false,
    DateTime? AiAnalysisRetryBlockedAtUtc = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SingleAnalyzerStatusResponse FromRecord(SingleContentAnalysisRecord record)
    {
        SingleAnalyzerContentSnapshotResponse? snapshot = null;
        if (!string.IsNullOrWhiteSpace(record.ContentSnapshotJson))
        {
            snapshot = JsonSerializer.Deserialize<SingleAnalyzerContentSnapshotResponse>(
                record.ContentSnapshotJson, JsonOptions);
        }

        SingleAnalyzerAnalysisResponse? analysis = null;
        if (!string.IsNullOrWhiteSpace(record.AnalysisJson))
        {
            analysis = JsonSerializer.Deserialize<SingleAnalyzerAnalysisResponse>(
                record.AnalysisJson, JsonOptions);
        }

        return new SingleAnalyzerStatusResponse(
            record.Id,
            record.OrganizationId,
            record.ClientId,
            record.SourceUrl,
            record.NormalizedUrl,
            record.SourcePlatformKey ?? string.Empty,
            record.Status,
            snapshot,
            analysis,
            record.FailureCode,
            record.FailureMessageKey,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.CompletedAtUtc,
            record.AiAnalysisFailedAttemptCount,
            record.AiAnalysisFailedAttemptCount >= SingleContentAnalyzer.MaxAiAnalysisFailedAttempts,
            record.AiAnalysisRetryBlockedAtUtc);
    }
}

public sealed record SingleAnalyzerContentSnapshotResponse(
    string PlatformKey,
    string CanonicalUrl,
    string? Title,
    string? Description,
    string? Author,
    string? ThumbnailUrl,
    int? DurationSeconds,
    IReadOnlyDictionary<string, long> Metrics,
    string? Transcript,
    IReadOnlyList<SingleAnalyzerMediaReferenceResponse> MediaReferences,
    string? RawPayloadReference,
    IReadOnlyList<string> Tags,
    string? ContentType,
    DateTimeOffset? PublishedAtUtc)
{
    public static SingleAnalyzerContentSnapshotResponse FromDomain(SingleAnalyzerContentSnapshot snapshot) =>
        new(
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
                .Select(x => new SingleAnalyzerMediaReferenceResponse(x.Kind, x.Uri, x.MimeType))
                .ToArray(),
            snapshot.RawPayloadReference,
            snapshot.Tags,
            snapshot.ContentType,
            snapshot.PublishedAtUtc);
}

public sealed record SingleAnalyzerMediaReferenceResponse(
    string Kind,
    string Uri,
    string? MimeType);

/// <summary>
/// Response DTO for the analysis portion of a completed single-content analysis.
/// <see cref="Patterns"/> is nullable — null for existing analyses produced before prompt v2.
/// </summary>
public sealed record SingleAnalyzerAnalysisResponse(
    string Summary,
    IReadOnlyList<string> DetectedHooks,
    string LikelyAudience,
    string NarrativeStructure,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<SingleAnalyzerIdeaResponse> DerivedIdeas,
    IReadOnlyList<string> ClientAdaptationRecommendations,
    double Confidence,
    IReadOnlyList<string> Warnings,
    PatternSet? Patterns = null)
{
    public static SingleAnalyzerAnalysisResponse FromDomain(SingleAnalyzerAnalysis analysis) =>
        new(
            analysis.Summary,
            analysis.DetectedHooks,
            analysis.LikelyAudience,
            analysis.NarrativeStructure,
            analysis.Strengths,
            analysis.Weaknesses,
            analysis.DerivedIdeas.Select(SingleAnalyzerIdeaResponse.FromDomain).ToArray(),
            analysis.ClientAdaptationRecommendations,
            analysis.Confidence,
            analysis.Warnings,
            analysis.Patterns);
}

public sealed record SingleAnalyzerIdeaResponse(
    string Title,
    string Concept,
    string Rationale,
    SingleAnalyzerScoreResponse Score)
{
    public static SingleAnalyzerIdeaResponse FromDomain(SingleAnalyzerIdea idea) =>
        new(
            idea.Title,
            idea.Concept,
            idea.Rationale,
            SingleAnalyzerScoreResponse.FromDomain(idea.Score));
}

public sealed record SingleAnalyzerScoreResponse(
    int Novelty,
    int ClientFit,
    int PlatformFit,
    int HookQuality,
    int Feasibility,
    int ExpectedEngagement)
{
    public static SingleAnalyzerScoreResponse FromDomain(SingleAnalyzerScore score) =>
        new(
            score.Novelty,
            score.ClientFit,
            score.PlatformFit,
            score.HookQuality,
            score.Feasibility,
            score.ExpectedEngagement);
}

public sealed record SingleAnalyzerWarningResponse(
    string Code,
    string MessageKey)
{
    public static SingleAnalyzerWarningResponse FromContentWarning(SocialContentWarning warning) =>
        new(warning.Code, warning.MessageKey);
}
