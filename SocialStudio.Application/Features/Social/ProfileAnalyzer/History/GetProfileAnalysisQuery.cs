using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ProfileAnalyzer.Analyze;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ProfileAnalysis;
using MediatR;

namespace SocialStudio.Application.Features.Social.ProfileAnalyzer.History;

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
