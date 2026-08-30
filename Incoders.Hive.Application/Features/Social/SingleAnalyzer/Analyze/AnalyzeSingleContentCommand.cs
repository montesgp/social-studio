using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;

public sealed record AnalyzeSingleContentCommand(
    Guid OrganizationId,
    Guid UserId,
    string Url,
    Guid? ClientId,
    string? PreferredExtractionProviderKey) : IRequest<Result<SingleAnalyzerStatusResponse>>;

public sealed record GetSingleContentAnalysisQuery(
    Guid AnalysisId,
    Guid OrganizationId) : IRequest<Result<SingleAnalyzerStatusResponse>>;

/// <summary>
/// Re-runs ONLY the AI stage of a previously-failed analysis, reusing the persisted snapshot.
/// No extraction call → no extra Apify cost, no extra tokens against the scrape provider.
/// </summary>
public sealed record RetryAiAnalysisCommand(
    Guid AnalysisId,
    Guid OrganizationId) : IRequest<Result<SingleAnalyzerStatusResponse>>;
