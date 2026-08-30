using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.SingleAnalyzer.History;

/// <summary>
/// Pre-flight lookup used before queueing a new single-analyzer run.
/// It is scoped to the current organization and user, matching the saved-analysis history.
/// </summary>
public sealed record FindSingleContentAnalysisByUrlQuery(
    Guid OrganizationId,
    Guid UserId,
    string Url) : IRequest<Result<FindSingleContentAnalysisByUrlResponse>>;

public sealed record FindSingleContentAnalysisByUrlResponse(
    SingleContentAnalysisHistoryItemResponse? Item);

public sealed class FindSingleContentAnalysisByUrlHandler
    : IRequestHandler<FindSingleContentAnalysisByUrlQuery, Result<FindSingleContentAnalysisByUrlResponse>>
{
    private readonly ISingleContentAnalysisRepository _analyses;

    public FindSingleContentAnalysisByUrlHandler(ISingleContentAnalysisRepository analyses)
    {
        _analyses = analyses;
    }

    public async Task<Result<FindSingleContentAnalysisByUrlResponse>> Handle(
        FindSingleContentAnalysisByUrlQuery request,
        CancellationToken cancellationToken)
    {
        var sourceUrl = SingleAnalyzerSourceUrl.Create(request.Url);
        if (sourceUrl.IsFailure)
        {
            return Result.Failure<FindSingleContentAnalysisByUrlResponse>(sourceUrl.Error);
        }

        var match = await _analyses.FindLatestByUserAndUrlAsync(
            request.OrganizationId,
            request.UserId,
            sourceUrl.Value.NormalizedUrl,
            sourceUrl.Value.OriginalUrl,
            cancellationToken);

        return Result.Success(new FindSingleContentAnalysisByUrlResponse(
            match is null ? null : SingleContentAnalysisHistoryMapping.ToResponse(match)));
    }
}
