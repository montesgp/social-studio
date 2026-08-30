using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ProfileAnalyzer.History;

/// <summary>Poll query — returns current status of a single profile analysis (PA-15).</summary>
public sealed record GetProfileAnalysisQuery(
    Guid AnalysisId,
    Guid OrganizationId) : IRequest<Result<AnalyzeProfileStatusResponse>>;

public sealed class GetProfileAnalysisQueryHandler
    : IRequestHandler<GetProfileAnalysisQuery, Result<AnalyzeProfileStatusResponse>>
{
    private readonly IProfileAnalysisRepository _repository;

    public GetProfileAnalysisQueryHandler(IProfileAnalysisRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<AnalyzeProfileStatusResponse>> Handle(
        GetProfileAnalysisQuery request,
        CancellationToken cancellationToken)
    {
        var analysis = await _repository.GetByIdAsync(
            request.AnalysisId,
            request.OrganizationId,
            cancellationToken);

        return analysis is null
            ? Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.NotFound)
            : Result.Success(AnalyzeProfileStatusResponse.FromRecord(analysis));
    }
}
