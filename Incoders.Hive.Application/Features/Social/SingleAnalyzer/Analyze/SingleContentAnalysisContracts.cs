using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;

public interface ISingleContentAnalyzer
{
    Task<Result<SingleContentAnalysisResponse>> AnalyzeAsync(
        SingleContentAnalysisRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SingleContentAnalysisRequest(
    Guid OrganizationId,
    Guid UserId,
    string Url,
    Guid? ClientId,
    string? PreferredExtractionProviderKey,
    string? PreferredAiProviderKey,
    string? PreferredAiModel);

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
    string? RawPayloadReference)
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
            snapshot.RawPayloadReference);
}

public sealed record SingleAnalyzerMediaReferenceResponse(
    string Kind,
    string Uri,
    string? MimeType);

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
    IReadOnlyList<string> Warnings)
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
            analysis.Warnings);
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
