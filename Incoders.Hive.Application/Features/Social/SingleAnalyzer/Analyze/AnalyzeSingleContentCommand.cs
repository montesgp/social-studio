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
